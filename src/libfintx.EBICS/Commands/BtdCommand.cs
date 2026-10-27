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
    /// <summary>BTD: EBICS 3.0 download of a business transaction format.</summary>
    internal class BtdCommand : SegmentedDownloadCommand<BtdResponse>
    {
        internal BtdParams Params { private get; set; }
        internal override string OrderType => "BTD";
        internal override string OrderAttribute => null;

        protected override string SecurityMedium => Params.SecurityMedium;
        protected override bool HasData => Response.Data != null;

        protected override void SetData(byte[] data, int numSegments)
        {
            Response.Data = data;
            Response.NumSegments = numSegments;
        }

        protected override IXElementSerializer CreateOrderParams() => new BtdOrderParams
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
        };
    }
}
