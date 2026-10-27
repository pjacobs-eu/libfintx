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
using libfintx.EBICS.Parameters;
using libfintx.EBICS.Responses;
using libfintx.Xml;

namespace libfintx.EBICS.Commands
{
    /// <summary>
    /// HTD (subscriber information) and HKD (customer information with all subscribers): the
    /// accounts, permitted order types and permissions as the bank holds them. The order data is
    /// an XML document (HTDResponseOrderData / HKDResponseOrderData) in the EBICS namespace.
    ///
    /// EBICS 2.5 sends them with OrderAttribute DZHNN, EBICS 3.0 as AdminOrderType. Both need an
    /// (empty) StandardOrderParams: the H005 schema requires an OrderParams element in the static
    /// header, and banks reject the request without it (061002 EBICS_INVALID_REQUEST).
    /// </summary>
    internal class CustomerInfoCommand<T> : SegmentedDownloadCommand<T> where T : CustomerInfoResponse, new()
    {
        private readonly string _orderType;

        internal CustomerInfoCommand(string orderType)
        {
            _orderType = orderType;
        }

        internal Params Params { private get; set; }
        internal override string OrderType => _orderType;
        internal override string OrderAttribute => Namespaces.IsH005 ? null : "DZHNN";

        protected override string SecurityMedium => Params.SecurityMedium;
        protected override bool HasData => Response.Data != null;

        protected override void SetData(byte[] data, int numSegments) => Response.Data = data;

        protected override IXElementSerializer CreateOrderParams() => new EmptyOrderParams { Namespaces = Namespaces };
    }
}
