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
namespace libfintx.EBICS.Responses
{
    public class BtdResponse : Response
    {
        /// <summary>
        /// The decrypted and decompressed order data: with a ZIP container the archive itself.
        /// Null if the bank had nothing to deliver (090005 EBICS_NO_DOWNLOAD_DATA_AVAILABLE).
        /// </summary>
        public byte[] Data { get; internal set; }

        public int NumSegments { get; internal set; }
    }
}
