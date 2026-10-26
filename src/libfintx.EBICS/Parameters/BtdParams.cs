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

namespace libfintx.EBICS.Parameters
{
    /// <summary>
    /// EBICS 3.0 download of a business transaction format (BTF). The values come from the bank's
    /// BTF table; for the German end-of-day statement this is typically
    /// ServiceName "EOP", Scope "DE", MsgName "camt.053", Container "ZIP".
    /// </summary>
    public class BtdParams : Params
    {
        public string ServiceName { get; set; }
        public string Scope { get; set; }
        public string ServiceOption { get; set; }
        public string ContainerType { get; set; }
        public string MsgName { get; set; }
        public string MsgNameVersion { get; set; }
        public string MsgNameVariant { get; set; }
        public string MsgNameFormat { get; set; }

        /// <summary>Optional; without a range the bank delivers what has not been fetched yet.</summary>
        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        /// <summary>German end-of-day statement camt.053 in a ZIP container.</summary>
        public static BtdParams Camt053(DateTime? startDate = null, DateTime? endDate = null) => new BtdParams
        {
            ServiceName = "EOP",
            Scope = "DE",
            MsgName = "camt.053",
            ContainerType = "ZIP",
            StartDate = startDate,
            EndDate = endDate
        };
    }
}
