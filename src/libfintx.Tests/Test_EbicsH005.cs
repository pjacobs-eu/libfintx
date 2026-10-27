using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using libfintx.EBICS;
using libfintx.EBICS.Parameters;
using libfintx.EBICSConfig;
using libfintx.Xml;
using Xunit;

namespace libfintx.Tests
{
    /// <summary>
    /// EBICS 3.0 (H005): key management with X.509 certificates, AdminOrderType, certificate
    /// digests and the BTD download — against a fake bank on localhost.
    /// </summary>
    public class Test_EbicsH005
    {
        private const string H005 = "urn:org:ebics:H005";
        private const string Dsig = "http://www.w3.org/2000/09/xmldsig#";
        private static readonly XNamespace Ns = H005;
        private static readonly XNamespace NsDsig = Dsig;

        private static NamespaceConfig Namespaces(EbicsVersion version) => new NamespaceConfig
        {
            Ebics = $"urn:org:ebics:{version}",
            XmlDsig = Dsig,
            SignatureData = version == EbicsVersion.H005 ? "http://www.ebics.org/S002" : "http://www.ebics.org/S001",
            Version = version
        };

        private static X509Certificate2 Certificate(string name, bool withPrivateKey = true)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
            return withPrivateKey ? cert : new X509Certificate2(cert.RawData);
        }

        [Fact]
        public void Signature_key_is_sent_as_certificate_in_S002()
        {
            var keys = new SignKeyPair { Certificate = Certificate("sign"), Version = SignVersion.A006 };

            var xml = new SignaturePubKeyOrderData
            {
                Namespaces = Namespaces(EbicsVersion.H005), PartnerId = "PARTNER", UserId = "USER", SignKeys = keys
            }.Serialize();

            XNamespace s002 = "http://www.ebics.org/S002";
            Assert.Equal(s002 + "SignaturePubKeyOrderData", xml.Name);
            Assert.Null(xml.Descendants(s002 + "PubKeyValue").FirstOrDefault());
            var certificate = xml.Descendants(NsDsig + "X509Certificate").Single().Value;
            Assert.Equal(keys.Certificate.RawData, Convert.FromBase64String(certificate));
            Assert.Equal("A006", xml.Descendants(s002 + "SignatureVersion").Single().Value);

            // XML-DSig: serial number as decimal integer
            var serial = xml.Descendants(NsDsig + "X509SerialNumber").Single().Value;
            Assert.True(serial.All(char.IsDigit));
        }

        [Fact]
        public void H004_keeps_PubKeyValue()
        {
            var keys = new AuthKeyPair { Certificate = Certificate("auth"), Version = AuthVersion.X002 };
            var xml = new AuthenticationPubKeyInfo
            {
                Namespaces = Namespaces(EbicsVersion.H004), AuthKeys = keys, UseEbicsDefaultNamespace = true
            }.Serialize();

            Assert.NotNull(xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "PubKeyValue"));
            Assert.Null(xml.Descendants(NsDsig + "X509Data").FirstOrDefault());
        }

        [Fact]
        public void Order_details_use_AdminOrderType_without_attribute()
        {
            var xml = new OrderDetails
            {
                Namespaces = Namespaces(EbicsVersion.H005), OrderType = "INI", OrderAttribute = "DZNNN"
            }.Serialize();

            Assert.Equal("INI", xml.Element(Ns + "AdminOrderType")?.Value);
            Assert.Null(xml.Element(Ns + "OrderType"));
            Assert.Null(xml.Element(Ns + "OrderAttribute"));
        }

        [Fact]
        public void Btd_order_params_follow_the_schema_order()
        {
            var xml = new BtdOrderParams
            {
                Namespaces = Namespaces(EbicsVersion.H005), ServiceName = "EOP", Scope = "DE", ContainerType = "ZIP",
                MsgName = "camt.053", MsgNameVersion = "08",
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 26)
            }.Serialize();

            var service = xml.Element(Ns + "Service");
            Assert.Equal(new[] { "ServiceName", "Scope", "Container", "MsgName" },
                service.Elements().Select(e => e.Name.LocalName));
            Assert.Equal("ZIP", service.Element(Ns + "Container").Attribute("containerType").Value);
            Assert.Equal("08", service.Element(Ns + "MsgName").Attribute("version").Value);
            Assert.Equal("2026-10-01", xml.Element(Ns + "DateRange").Element(Ns + "Start").Value);
        }

        [Fact]
        public void LastSegment_is_an_xml_boolean()
        {
            var xml = new MutableHeader
            {
                Namespaces = Namespaces(EbicsVersion.H005), TransactionPhase = "Transfer", SegmentNumber = 2, LastSegment = true
            }.Serialize();

            Assert.Equal("true", xml.Element(Ns + "SegmentNumber").Attribute("lastSegment").Value);
        }

        [Fact]
        public void Certificate_digest_is_sha256_over_der_and_survives_pem_round_trip()
        {
            var cert = Certificate("bank", withPrivateKey: false);
            var keys = new CryptKeyPair { Certificate = cert, Version = CryptVersion.E002 };

            byte[] pem = null;
            keys.Save(b => pem = b);
            var loaded = new CryptKeyPair { Version = CryptVersion.E002 };
            loaded.Load(pem);

            Assert.Equal(SHA256.HashData(cert.RawData), loaded.DigestFor(EbicsVersion.H005));
            Assert.Equal(keys.Digest, loaded.DigestFor(EbicsVersion.H004));
        }

        // ── Fake bank ──────────────────────────────────────────────────────────────────

        private sealed class Keys
        {
            public X509Certificate2 Sign = Certificate("user sign");
            public X509Certificate2 Auth = Certificate("user auth");
            public X509Certificate2 Crypt = Certificate("user crypt");
            public X509Certificate2 BankAuth = Certificate("bank auth");
            public X509Certificate2 BankCrypt = Certificate("bank crypt");
        }

        private static Config ConfigFor(string address, Keys k, bool withBankKeys) => new Config
        {
            Address = address,
            Version = EbicsVersion.H005,
            User = new UserParams
            {
                HostId = "HOST",
                PartnerId = "PARTNER",
                UserId = "USER",
                SignKeys = new SignKeyPair { Certificate = k.Sign, Version = SignVersion.A006 },
                AuthKeys = new AuthKeyPair { Certificate = k.Auth, Version = AuthVersion.X002 },
                CryptKeys = new CryptKeyPair { Certificate = k.Crypt, Version = CryptVersion.E002 }
            },
            Bank = withBankKeys
                ? new BankParams
                {
                    AuthKeys = new AuthKeyPair { Certificate = new X509Certificate2(k.BankAuth.RawData), Version = AuthVersion.X002 },
                    CryptKeys = new CryptKeyPair { Certificate = new X509Certificate2(k.BankCrypt.RawData), Version = CryptVersion.E002 }
                }
                : null
        };

        /// <summary>Answers each request with the next handler; records the requests.</summary>
        private sealed class FakeBank : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            private readonly Queue<Func<XDocument, string>> _handlers;
            private readonly Task _loop;
            public List<XDocument> Requests { get; } = new List<XDocument>();
            public string Address { get; }

            public FakeBank(params Func<XDocument, string>[] handlers)
            {
                _handlers = new Queue<Func<XDocument, string>>(handlers);
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                Address = $"http://127.0.0.1:{port}/ebics/";
                _listener.Prefixes.Add(Address);
                _listener.Start();
                _loop = Task.Run(Serve);
            }

            private async Task Serve()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await _listener.GetContextAsync();
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    string body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync();
                    }

                    var doc = XDocument.Parse(body);
                    Requests.Add(doc);
                    var response = _handlers.Count > 0 ? _handlers.Dequeue()(doc) : "<error/>";
                    var bytes = Encoding.UTF8.GetBytes(response);
                    ctx.Response.ContentType = "text/xml";
                    await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
            }

            public void Dispose()
            {
                _listener.Stop();
                _listener.Close();
            }
        }

        /// <summary>Compress, encrypt (AES-128-CBC, transaction key for our E002 key) — as a bank does.</summary>
        private static (string OrderData, string TransactionKey, string Digest) Encrypt(byte[] payload, X509Certificate2 userCrypt)
        {
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
                {
                    zlib.Write(payload, 0, payload.Length);
                }

                compressed = output.ToArray();
            }

            using var aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.ANSIX923;
            aes.IV = new byte[16];
            var encrypted = aes.CreateEncryptor().TransformFinalBlock(compressed, 0, compressed.Length);

            using var rsa = userCrypt.GetRSAPublicKey();
            var transactionKey = rsa.Encrypt(aes.Key, RSAEncryptionPadding.Pkcs1);

            return (Convert.ToBase64String(encrypted), Convert.ToBase64String(transactionKey),
                Convert.ToBase64String(SHA256.HashData(userCrypt.RawData)));
        }

        private static string Response(string root, string phase, int? segment, bool lastSegment, string techCode,
            string busCode, string transactionId = null, int? numSegments = null, XElement dataTransfer = null)
        {
            var mutable = new XElement(Ns + "mutable", new XElement(Ns + "TransactionPhase", phase));
            if (segment.HasValue)
            {
                mutable.Add(new XElement(Ns + "SegmentNumber", new XAttribute("lastSegment", lastSegment ? "true" : "false"), segment));
            }

            mutable.Add(new XElement(Ns + "ReturnCode", techCode), new XElement(Ns + "ReportText", "[EBICS_OK] OK"));

            var staticHeader = new XElement(Ns + "static");
            if (transactionId != null) staticHeader.Add(new XElement(Ns + "TransactionID", transactionId));
            if (numSegments.HasValue) staticHeader.Add(new XElement(Ns + "NumSegments", numSegments));

            var body = new XElement(Ns + "body");
            if (dataTransfer != null) body.Add(dataTransfer);
            body.Add(new XElement(Ns + "ReturnCode", busCode));

            return new XDocument(new XElement(Ns + root, new XAttribute("Version", "H005"), new XAttribute("Revision", "1"),
                new XElement(Ns + "header", new XAttribute("authenticate", "true"), staticHeader, mutable),
                body)).ToString(SaveOptions.DisableFormatting);
        }

        private static XElement DataTransfer(string orderData, string transactionKey = null, string digest = null)
        {
            var x = new XElement(Ns + "DataTransfer");
            if (transactionKey != null)
            {
                x.Add(new XElement(Ns + "DataEncryptionInfo", new XAttribute("authenticate", "true"),
                    new XElement(Ns + "EncryptionPubKeyDigest", new XAttribute("Version", "E002"),
                        new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256"), digest),
                    new XElement(Ns + "TransactionKey", transactionKey)));
            }

            x.Add(new XElement(Ns + "OrderData", orderData));
            return x;
        }

        private static byte[] SampleZip()
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(zip.CreateEntry("camt053.xml").Open());
                // big enough to be worth two segments in the test, and not a multiple of anything
                writer.Write("<Document>" + string.Concat(Enumerable.Range(0, 500).Select(i => $"<Ntry n=\"{i}\"/>")) + "</Document>");
            }

            return buffer.ToArray();
        }

        [Fact]
        public void Btd_downloads_two_segments_joins_them_and_acknowledges()
        {
            var keys = new Keys();
            var zip = SampleZip();
            var (orderData, transactionKey, digest) = Encrypt(zip, keys.Crypt);
            // Split the base64 text at an odd position: the segments only make sense joined.
            var cut = orderData.Length / 2 + 1;

            using var bank = new FakeBank(
                _ => Response("ebicsResponse", "Initialisation", 1, false, "000000", "000000", "TX01", 2,
                    DataTransfer(orderData.Substring(0, cut), transactionKey, digest)),
                _ => Response("ebicsResponse", "Transfer", 2, true, "000000", "000000", "TX01", null,
                    DataTransfer(orderData.Substring(cut))),
                _ => Response("ebicsResponse", "Receipt", null, false, "011000", "000000", "TX01"));

            var client = EbicsClient.Factory().Create(ConfigFor(bank.Address, keys, withBankKeys: true));
            var response = client.BTD(BtdParams.Camt053(new DateTime(2026, 10, 1), new DateTime(2026, 10, 26)));

            Assert.Equal(11000, response.TechnicalReturnCode); // EBICS_DOWNLOAD_POSTPROCESS_DONE from the receipt
            Assert.Equal(zip, response.Data);
            Assert.Equal(2, response.NumSegments);
            Assert.Equal(3, bank.Requests.Count);

            var init = bank.Requests[0];
            Assert.Equal("BTD", init.Descendants(Ns + "AdminOrderType").Single().Value);
            Assert.Empty(init.Descendants(Ns + "OrderAttribute"));
            Assert.Equal("EOP", init.Descendants(Ns + "ServiceName").Single().Value);
            Assert.Equal("ZIP", init.Descendants(Ns + "Container").Single().Attribute("containerType").Value);
            Assert.Equal(Convert.ToBase64String(SHA256.HashData(keys.BankAuth.RawData)),
                init.Descendants(Ns + "Authentication").Single().Value);
            Assert.NotEmpty(init.Descendants(NsDsig + "SignatureValue"));

            var transfer = bank.Requests[1];
            Assert.Equal("TX01", transfer.Descendants(Ns + "TransactionID").Single().Value);
            Assert.Equal("true", transfer.Descendants(Ns + "SegmentNumber").Single().Attribute("lastSegment").Value);

            Assert.Equal("0", bank.Requests[2].Descendants(Ns + "ReceiptCode").Single().Value);
        }

        [Fact]
        public void Btd_without_new_data_returns_no_data_and_sends_no_receipt()
        {
            var keys = new Keys();
            using var bank = new FakeBank(
                _ => Response("ebicsResponse", "Initialisation", null, false, "000000", "090005"));

            var client = EbicsClient.Factory().Create(ConfigFor(bank.Address, keys, withBankKeys: true));
            var response = client.BTD(BtdParams.Camt053());

            Assert.Equal(90005, response.BusinessReturnCode);
            Assert.Null(response.Data);
            Assert.Single(bank.Requests);
        }

        [Fact]
        public void Htd_downloads_the_subscriber_information_with_empty_order_params()
        {
            var keys = new Keys();
            var xml = System.Text.Encoding.UTF8.GetBytes(
                "<HTDResponseOrderData xmlns=\"urn:org:ebics:H005\"><PartnerInfo><AddressInfo><Name>Test</Name>" +
                "</AddressInfo></PartnerInfo></HTDResponseOrderData>");
            var (orderData, transactionKey, digest) = Encrypt(xml, keys.Crypt);

            using var bank = new FakeBank(
                _ => Response("ebicsResponse", "Initialisation", 1, true, "000000", "000000", "TX02", 1,
                    DataTransfer(orderData, transactionKey, digest)),
                _ => Response("ebicsResponse", "Receipt", null, false, "011000", "000000", "TX02"));

            var client = EbicsClient.Factory().Create(ConfigFor(bank.Address, keys, withBankKeys: true));
            var response = client.HTD(new HtdParams());

            Assert.Equal(xml, response.Data);
            Assert.Equal(2, bank.Requests.Count);
            var init = bank.Requests[0];
            Assert.Equal("HTD", init.Descendants(Ns + "AdminOrderType").Single().Value);
            Assert.Empty(init.Descendants(Ns + "OrderAttribute"));
            // The H005 schema requires an OrderParams element; without it banks answer 061002.
            var orderParams = init.Descendants(Ns + "OrderDetails").Single().Elements().Last();
            Assert.Equal(Ns + "StandardOrderParams", orderParams.Name);
            Assert.False(orderParams.HasElements);
            Assert.Equal("0", bank.Requests[1].Descendants(Ns + "ReceiptCode").Single().Value);
        }

        [Fact]
        public void Hkd_uses_its_own_order_type()
        {
            var keys = new Keys();
            using var bank = new FakeBank(
                _ => Response("ebicsResponse", "Initialisation", null, false, "000000", "090005"));

            var client = EbicsClient.Factory().Create(ConfigFor(bank.Address, keys, withBankKeys: true));
            var response = client.HKD(new HkdParams());

            Assert.Null(response.Data);
            Assert.Equal("HKD", bank.Requests.Single().Descendants(Ns + "AdminOrderType").Single().Value);
        }

        /// <summary>The official H005 schemas shipped with libfintx.Xsd, found from the test output upwards.</summary>
        private static XmlSchemaSet RequestSchemas()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "libfintx.Xsd", "H005")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            var folder = Path.Combine(dir.FullName, "libfintx.Xsd", "H005");
            var schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
            // xmldsig-core-schema.xsd carries a DTD, which an import would refuse — so it goes in first,
            // with its internal subset parsed and the external XMLSchema.dtd (not shipped) skipped.
            using (var dsig = XmlReader.Create(Path.Combine(folder, "xmldsig-core-schema.xsd"),
                       new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null }))
            {
                schemas.Add(null, dsig);
            }

            schemas.Add(null, Path.Combine(folder, "ebics_request_H005.xsd"));

            schemas.Compile();
            return schemas;
        }

        private static void AssertValid(XmlSchemaSet schemas, XDocument request)
        {
            var errors = new List<string>();
            request.Validate(schemas, (_, e) => errors.Add(e.Message));
            Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n" + request);
        }

        [Fact]
        public void Download_requests_conform_to_the_h005_schema()
        {
            var keys = new Keys();
            var schemas = RequestSchemas();
            var (orderData, transactionKey, digest) = Encrypt(Encoding.UTF8.GetBytes("<a/>"), keys.Crypt);
            var cut = orderData.Length / 2;

            foreach (var send in new Func<IEbicsClient, object>[]
                     {
                         c => c.HTD(new HtdParams()),
                         c => c.HKD(new HkdParams()),
                         c => c.BTD(BtdParams.Camt053(new DateTime(2026, 10, 1), new DateTime(2026, 10, 26))),
                         c => c.BTD(new BtdParams { ServiceName = "STM", Scope = "DE", MsgName = "camt.052", ContainerType = "ZIP" })
                     })
            {
                using var bank = new FakeBank(
                    _ => Response("ebicsResponse", "Initialisation", 1, false, "000000", "000000", "0123456789ABCDEF0123456789ABCDEF", 2,
                        DataTransfer(orderData.Substring(0, cut), transactionKey, digest)),
                    _ => Response("ebicsResponse", "Transfer", 2, true, "000000", "000000", "0123456789ABCDEF0123456789ABCDEF", null,
                        DataTransfer(orderData.Substring(cut))),
                    _ => Response("ebicsResponse", "Receipt", null, false, "011000", "000000", "0123456789ABCDEF0123456789ABCDEF"));

                send(EbicsClient.Factory().Create(ConfigFor(bank.Address, keys, withBankKeys: true)));

                Assert.Equal(3, bank.Requests.Count); // Initialisierung, Transfer, Quittung
                foreach (var request in bank.Requests)
                {
                    AssertValid(schemas, request);
                }
            }
        }

        [Fact]
        public void Hpb_reads_bank_certificates()
        {
            var keys = new Keys();
            XNamespace ns = H005;
            var orderData = new XDocument(new XElement(ns + "HPBResponseOrderData",
                new XAttribute(XNamespace.Xmlns + "ds", Dsig),
                new XElement(ns + "AuthenticationPubKeyInfo",
                    new XElement(NsDsig + "X509Data", new XElement(NsDsig + "X509Certificate", Convert.ToBase64String(keys.BankAuth.RawData))),
                    new XElement(ns + "AuthenticationVersion", "X002")),
                new XElement(ns + "EncryptionPubKeyInfo",
                    new XElement(NsDsig + "X509Data", new XElement(NsDsig + "X509Certificate", Convert.ToBase64String(keys.BankCrypt.RawData))),
                    new XElement(ns + "EncryptionVersion", "E002")),
                new XElement(ns + "HostID", "HOST"))).ToString(SaveOptions.DisableFormatting);
            var (data, transactionKey, digest) = Encrypt(Encoding.UTF8.GetBytes(orderData), keys.Crypt);

            using var bank = new FakeBank(
                _ => Response("ebicsKeyManagementResponse", "Initialisation", null, false, "000000", "000000",
                    dataTransfer: DataTransfer(data, transactionKey, digest)));

            var client = EbicsClient.Factory().Create(ConfigFor(bank.Address, keys, withBankKeys: false));
            var response = client.HPB(new HpbParams());

            Assert.Equal(SHA256.HashData(keys.BankAuth.RawData), response.Bank.AuthKeys.DigestFor(EbicsVersion.H005));
            Assert.Equal(SHA256.HashData(keys.BankCrypt.RawData), response.Bank.CryptKeys.DigestFor(EbicsVersion.H005));
            Assert.Equal("HPB", bank.Requests[0].Descendants(Ns + "AdminOrderType").Single().Value);
        }

        [Fact]
        public void Ini_sends_the_signature_certificate()
        {
            var keys = new Keys();
            using var bank = new FakeBank(
                _ => Response("ebicsKeyManagementResponse", "Initialisation", null, false, "000000", "000000"));

            var client = EbicsClient.Factory().Create(ConfigFor(bank.Address, keys, withBankKeys: false));
            var response = client.INI(new IniParams());

            Assert.Equal(0, response.TechnicalReturnCode);
            var request = bank.Requests.Single();
            Assert.Equal("INI", request.Descendants(Ns + "AdminOrderType").Single().Value);

            var compressed = Convert.FromBase64String(request.Descendants(Ns + "OrderData").Single().Value);
            using var input = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            var orderData = XDocument.Load(input);
            Assert.Equal("http://www.ebics.org/S002", orderData.Root.Name.NamespaceName);
            Assert.Equal(keys.Sign.RawData,
                Convert.FromBase64String(orderData.Descendants(NsDsig + "X509Certificate").Single().Value));
        }
    }
}
