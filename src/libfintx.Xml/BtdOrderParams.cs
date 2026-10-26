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
using System.Xml.Linq;

namespace libfintx.Xml
{
    /// <summary>
    /// BTDOrderParams of an EBICS 3.0 download: the service (business transaction format) and an
    /// optional date range.
    /// </summary>
    public class BtdOrderParams : NamespaceAware, IXElementSerializer
    {
        public string ServiceName { private get; set; }
        public string Scope { private get; set; }
        public string ServiceOption { private get; set; }
        public string ContainerType { private get; set; }
        public string MsgName { private get; set; }
        public string MsgNameVersion { private get; set; }
        public string MsgNameVariant { private get; set; }
        public string MsgNameFormat { private get; set; }
        public DateTime? StartDate { private get; set; }
        public DateTime? EndDate { private get; set; }

        public XElement Serialize()
        {
            XNamespace nsEbics = Namespaces.Ebics;

            // Order as in RestrictedServiceType: ServiceName, Scope, ServiceOption, Container, MsgName.
            var service = new XElement(nsEbics + XmlNames.Service,
                new XElement(nsEbics + XmlNames.ServiceName, ServiceName));

            if (!string.IsNullOrWhiteSpace(Scope))
            {
                service.Add(new XElement(nsEbics + XmlNames.Scope, Scope));
            }

            if (!string.IsNullOrWhiteSpace(ServiceOption))
            {
                service.Add(new XElement(nsEbics + XmlNames.ServiceOption, ServiceOption));
            }

            if (!string.IsNullOrWhiteSpace(ContainerType))
            {
                service.Add(new XElement(nsEbics + XmlNames.Container,
                    new XAttribute(XmlNames.containerType, ContainerType)));
            }

            var msgName = new XElement(nsEbics + XmlNames.MsgName, MsgName);
            if (!string.IsNullOrWhiteSpace(MsgNameVersion))
            {
                msgName.Add(new XAttribute(XmlNames.version, MsgNameVersion));
            }

            if (!string.IsNullOrWhiteSpace(MsgNameVariant))
            {
                msgName.Add(new XAttribute(XmlNames.variant, MsgNameVariant));
            }

            if (!string.IsNullOrWhiteSpace(MsgNameFormat))
            {
                msgName.Add(new XAttribute(XmlNames.format, MsgNameFormat));
            }

            service.Add(msgName);

            var x = new XElement(nsEbics + XmlNames.BTDOrderParams, service);

            if (StartDate.HasValue && EndDate.HasValue)
            {
                x.Add(new XElement(nsEbics + XmlNames.DateRange,
                    new XElement(nsEbics + XmlNames.Start, StartDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XElement(nsEbics + XmlNames.End, EndDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                ));
            }

            return x;
        }
    }
}
