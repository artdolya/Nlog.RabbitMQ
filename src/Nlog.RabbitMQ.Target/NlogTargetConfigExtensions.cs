using System;
using System.Collections.Generic;
using System.Linq;

using NLog;
using NLog.Layouts;

using RabbitMQ.Client;

namespace Nlog.RabbitMQ.Target;

public static class NlogTargetConfigExtensions
{
    public static RabbitMqFactory GetRabbitMqFactoryFromConfig(this RabbitMqTarget target,
                                                               Func<Func<RabbitMqTarget, Layout>, string> layoutRenderer,
                                                               bool useSsl = false)
    {
        ConnectionFactory factory = new ConnectionFactory();
        string sslCertPath = layoutRenderer(t => t.SslCertPath);
        string sslCertPassphrase = layoutRenderer(t => t.SslCertPassphrase);

        Func<string, SslOption> sslOptionFactory = (hostName) =>
                                                   {
                                                       SslOption sslOption = new SslOption()
                                                                             {
                                                                                 Enabled = useSsl,
                                                                                 ServerName = hostName,
                                                                                 CertPath = sslCertPath,
                                                                                 CertPassphrase = sslCertPassphrase
                                                                             };
                                                       if (!string.IsNullOrEmpty(sslCertPath))
                                                       {
                                                           sslOption.CertPath = sslCertPath;
                                                       }

                                                       if (!string.IsNullOrEmpty(sslCertPassphrase))
                                                       {
                                                           sslOption.CertPassphrase = sslCertPassphrase;
                                                       }

                                                       return sslOption;
                                                   };
        
        string uri = layoutRenderer(t => t.Uri);
        IList<AmqpTcpEndpoint> endpoints = new List<AmqpTcpEndpoint>();
        if (!string.IsNullOrEmpty(uri))
        {
            factory.Uri = new Uri(uri);
            factory.Ssl = sslOptionFactory(factory.Uri.Host);
        }
        else
        {
            string[] hostNames = layoutRenderer(t => t.HostName).Split(',');
            if (!hostNames.Any())
            {
                throw new NLogConfigurationException("No hostname(s) provided");
            }
            
            factory.UserName = layoutRenderer(t => t.UserName);
            factory.Password = layoutRenderer(t => t.Password);
            factory.VirtualHost = layoutRenderer(t => t.VHost);
            factory.Port = int.Parse(layoutRenderer(t => t.Port));
            
            endpoints = hostNames.Where(h => !string.IsNullOrEmpty(h)).Select(h => new AmqpTcpEndpoint()
                                                                                      {
                                                                                          HostName = h.Trim(),
                                                                                          Port = factory.Port,
                                                                                          Ssl = sslOptionFactory(h)
                                                                                      }
                                                                              ).ToList();
        }
        
        string clientProvidedName = layoutRenderer(t => t.ClientProvidedName);
        
        if (!string.IsNullOrEmpty(clientProvidedName))
        {
            factory.ClientProvidedName = clientProvidedName;
        }
        
        return new RabbitMqFactory(factory, endpoints);
    }
}