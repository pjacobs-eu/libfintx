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
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using libfintx.EBICSConfig;

namespace libfintx.Xml
{
    /// <summary>
    /// ds:X509Data of a public key as EBICS 3.0 (H005) transmits it in INI and HIA:
    /// issuer and serial number plus the DER encoded certificate.
    /// </summary>
    public static class X509DataElement
    {
        public static XElement Create(NamespaceConfig namespaces, X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate), "EBICS H005 requires the key as X.509 certificate");
            }

            XNamespace nsDsig = namespaces.XmlDsig;

            return new XElement(nsDsig + XmlNames.X509Data,
                new XElement(nsDsig + XmlNames.X509IssuerSerial,
                    new XElement(nsDsig + XmlNames.X509IssuerName, certificate.Issuer),
                    new XElement(nsDsig + XmlNames.X509SerialNumber, SerialNumber(certificate))
                ),
                new XElement(nsDsig + XmlNames.X509Certificate, Convert.ToBase64String(certificate.RawData))
            );
        }

        /// <summary>XML-DSig wants the serial number as decimal integer, .NET gives it as hex.</summary>
        private static string SerialNumber(X509Certificate2 certificate)
        {
            var bytes = certificate.GetSerialNumber(); // little endian
            var withSign = new byte[bytes.Length + 1];
            Array.Copy(bytes, withSign, bytes.Length);
            return new BigInteger(withSign).ToString(CultureInfo.InvariantCulture);
        }
    }
}
