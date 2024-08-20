using System.Net;
using SIPSorcery.SIP.App;
using SIPSorcery.SIP;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Net;
using System.Net.Sockets;

namespace sip_sorcery_research;

public class SIPClient : IHostedService
{
    private SIPTransport _sipTransport;
    private SIPClientUserAgent _sipUserAgent;
    private bool _hasCallFailed;

    private readonly string _localhost = "localhost";

    public async Task StartAsync(CancellationToken cancellationToken)
    {

    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sipTransport.Dispose();
    }
}