/*	
 * 	
 *  This file is part of libfintx.
 *  
 *  Copyright (C) 2026 Philip Jacobs
 *  
 *  This program is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU Lesser General Public
 *  License as published by the Free Software Foundation; either
 *  version 3 of the License, or (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 *  Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with this program; if not, write to the Free Software Foundation,
 *  Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 * 	
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using libfintx.EBICS.Exceptions;
using libfintx.EBICS.Handler;
using libfintx.EBICS.Parameters;
using libfintx.EBICS.Responses;
using libfintx.Xml;

namespace libfintx.EBICS.Commands
{
    /// <summary>
    /// BTD: EBICS 3.0 download of a business transaction format.
    ///
    /// The bank compresses, encrypts and base64 encodes the order data once and splits the result
    /// into segments. The segments are therefore collected first and joined before decoding,
    /// decrypting and decompressing — a single segment on its own cannot be decrypted.
    /// </summary>
    internal class BtdCommand : GenericCommand<BtdResponse>
    {
        private static readonly ILogger s_logger = EbicsLogging.CreateLogger<BtdCommand>();

        private string _transactionId;
        private int _numSegments;
        private bool _initLastSegment;
        private byte[] _transactionKey;
        private string[] _segments;

        internal BtdParams Params { private get; set; }
        internal override string OrderType => "BTD";
        internal override string OrderAttribute => null;
        internal override TransactionType TransactionType => TransactionType.Download;
        internal override IList<XmlDocument> Requests => CreateRequests();
        internal override XmlDocument InitRequest => CreateInitRequest();
        internal override XmlDocument ReceiptRequest => _transactionId == null ? null : CreateReceiptRequest();

        internal override DeserializeResponse Deserialize(string payload)
        {
            using (new MethodLogger(s_logger))
            {
                try
                {
                    var dr = base.Deserialize(payload);
                    if (dr.HasError || dr.IsRecoverySync)
                    {
                        return dr;
                    }

                    var xph = new XPathHelper(XDocument.Parse(payload), Namespaces);

                    switch (dr.Phase)
                    {
                        case TransactionPhase.Initialisation:
                            _transactionId = dr.TransactionId;
                            _numSegments = Math.Max(dr.NumSegments, 1);
                            _initLastSegment = dr.LastSegment;
                            _segments = new string[_numSegments];
                            _transactionKey = DecryptTransactionKey(xph);
                            Store(dr.SegmentNumber, xph);
                            break;
                        case TransactionPhase.Transfer:
                            Store(dr.SegmentNumber, xph);
                            break;
                    }

                    if (Response.Data == null && _segments != null && _segments.All(s => s != null))
                    {
                        Response.Data = Assemble();
                        Response.NumSegments = _numSegments;
                    }

                    return dr;
                }
                catch (EbicsException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new DeserializationException($"can't deserialize {OrderType} command", ex, payload);
                }
            }
        }

        private void Store(int segmentNumber, XPathHelper xph)
        {
            if (segmentNumber < 1 || segmentNumber > _numSegments)
            {
                throw new DeserializationException($"segment {segmentNumber} outside of 1..{_numSegments}", xph.Xml);
            }

            _segments[segmentNumber - 1] = xph.GetOrderData()?.Value?.Trim()
                                           ?? throw new DeserializationException("OrderData missing", xph.Xml);
        }

        private byte[] Assemble()
        {
            var encrypted = Convert.FromBase64String(string.Concat(_segments));
            return Decompress(DecryptAES(encrypted, _transactionKey));
        }

        /// <summary>The transaction key from the first segment, after checking it was encrypted for our key.</summary>
        private byte[] DecryptTransactionKey(XPathHelper xph)
        {
            var digest = Convert.FromBase64String(xph.GetEncryptionPubKeyDigest()?.Value
                                                  ?? throw new DeserializationException("EncryptionPubKeyDigest missing", xph.Xml));
            var expected = Config.User.CryptKeys.DigestFor(Config.Version);
            if (!digest.SequenceEqual(expected))
            {
                throw new DeserializationException("order data was encrypted for another key", xph.Xml);
            }

            return DecryptRsa(Convert.FromBase64String(xph.GetTransactionKey()?.Value
                                                       ?? throw new DeserializationException("TransactionKey missing", xph.Xml)));
        }

        /// <summary>
        /// One transfer request per missing segment. The protocol handler picks the request by the
        /// number of the segment received last, so index n holds the request for segment n + 1.
        /// </summary>
        private IList<XmlDocument> CreateRequests()
        {
            using (new MethodLogger(s_logger))
            {
                if (_initLastSegment || _numSegments <= 1)
                {
                    return null;
                }

                try
                {
                    var reqs = new List<XmlDocument> { null };
                    for (var segment = 2; segment <= _numSegments; segment++)
                    {
                        var req = new EbicsRequest
                        {
                            Namespaces = Namespaces,
                            Version = Config.Version,
                            Revision = Config.Revision,
                            StaticHeader = new StaticHeader
                            {
                                Namespaces = Namespaces,
                                HostId = Config.User.HostId,
                                TransactionId = _transactionId
                            },
                            MutableHeader = new MutableHeader
                            {
                                Namespaces = Namespaces,
                                TransactionPhase = "Transfer",
                                SegmentNumber = segment,
                                LastSegment = segment == _numSegments
                            },
                            Body = new Body
                            {
                                Namespaces = Namespaces
                            }
                        };
                        reqs.Add(AuthenticateXml(req.Serialize().ToXmlDocument(), null, null));
                    }

                    return reqs;
                }
                catch (Exception ex) when (!(ex is EbicsException))
                {
                    throw new CreateRequestException($"can't create {OrderType} transfer requests", ex);
                }
            }
        }

        private XmlDocument CreateReceiptRequest()
        {
            using (new MethodLogger(s_logger))
            {
                try
                {
                    var receiptReq = new EbicsRequest
                    {
                        Version = Config.Version,
                        Revision = Config.Revision,
                        Namespaces = Namespaces,
                        StaticHeader = new StaticHeader
                        {
                            Namespaces = Namespaces,
                            HostId = Config.User.HostId,
                            TransactionId = _transactionId
                        },
                        MutableHeader = new MutableHeader
                        {
                            Namespaces = Namespaces,
                            TransactionPhase = "Receipt"
                        },
                        Body = new Body
                        {
                            Namespaces = Namespaces,
                            TransferReceipt = new TransferReceipt
                            {
                                Namespaces = Namespaces,
                                // 0 = received and processed; the bank marks the data as fetched.
                                ReceiptCode = Response.Data != null ? "0" : "1"
                            }
                        }
                    };
                    return AuthenticateXml(receiptReq.Serialize().ToXmlDocument(), null, null);
                }
                catch (Exception ex) when (!(ex is EbicsException))
                {
                    throw new CreateRequestException($"can't create {OrderType} receipt request", ex);
                }
            }
        }

        private XmlDocument CreateInitRequest()
        {
            using (new MethodLogger(s_logger))
            {
                try
                {
                    var initReq = new EbicsRequest
                    {
                        StaticHeader = new StaticHeader
                        {
                            Namespaces = Namespaces,
                            HostId = Config.User.HostId,
                            PartnerId = Config.User.PartnerId,
                            UserId = Config.User.UserId,
                            SecurityMedium = Params.SecurityMedium,
                            Nonce = CryptoUtils.GetNonce(),
                            Timestamp = CryptoUtils.GetUtcTimeNow(),
                            BankPubKeyDigests = new BankPubKeyDigests
                            {
                                Namespaces = Namespaces,
                                Bank = Config.Bank,
                                DigestAlgorithm = s_digestAlg
                            },
                            OrderDetails = new OrderDetails
                            {
                                Namespaces = Namespaces,
                                OrderType = OrderType,
                                StandardOrderParams = new BtdOrderParams
                                {
                                    Namespaces = Namespaces,
                                    ServiceName = Params.ServiceName,
                                    Scope = Params.Scope,
                                    ServiceOption = Params.ServiceOption,
                                    ContainerType = Params.ContainerType,
                                    MsgName = Params.MsgName,
                                    MsgNameVersion = Params.MsgNameVersion,
                                    MsgNameVariant = Params.MsgNameVariant,
                                    MsgNameFormat = Params.MsgNameFormat,
                                    StartDate = Params.StartDate,
                                    EndDate = Params.EndDate
                                }
                            }
                        },
                        MutableHeader = new MutableHeader
                        {
                            Namespaces = Namespaces,
                            TransactionPhase = "Initialisation"
                        },
                        Body = new Body
                        {
                            Namespaces = Namespaces
                        },
                        Namespaces = Namespaces,
                        Version = Config.Version,
                        Revision = Config.Revision
                    };
                    return AuthenticateXml(initReq.Serialize().ToXmlDocument(), null, null);
                }
                catch (Exception ex) when (!(ex is EbicsException))
                {
                    throw new CreateRequestException($"can't create {OrderType} init request", ex);
                }
            }
        }
    }
}
