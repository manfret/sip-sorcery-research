// Set up a default SIP transport.
using System.Net;
using System.Net.Sockets;
using System.Runtime.Intrinsics.Arm;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

await Task.Delay(2000);

var hasCallFailed = false;
var isCallHungup = false;
const string defaultDestinationSIPUri = "sip:helloworld@localhost:5080";
var callUri = SIPURI.ParseSIPURI(defaultDestinationSIPUri);
var exitMre = new ManualResetEvent(false);

var sipTransport = new SIPTransport();
sipTransport.ContactHost = "mew";
sipTransport.EnableTraceLogs();

var rtpSession = new VoIPMediaSession("C:\\Users\\Manfret\\Documents\\Repos\\sip-sorcery-research\\sample.wav");
var offerSDP = rtpSession.CreateOffer(IPAddress.Any);
//var offerSDP = rtpSession.CreateOffer(new IPAddress(new byte[]{1, 2, 3, 4}));

/*
v=0

    <username> задается вручную
    <sessionId> рандомное значение
    <sessionVersion> вначале 0, меняется с каждым reInvite/Update
    <nettype> IN = Internet
    <addrtype> IP4
    <unicast-address> 127.0.0.1 адрес, где была создана сессия
o=- 1044652801 0 IN IP4 127.0.0.1
    <session-name>
s=sipsorcery
    <nettype> IN = Internet
    <addrtype> IP4
    <connection-address> 10.0.0.2 берется из аргумента в CreateOffer или каким-то дефолтным способом в ядре программы
c=IN IP4 10.0.0.2
    <timestart> 0 - указывает что сессия активна с самого начала
    <timestop> 0 - указывает что сессия не имеет конца
t=0 0
    <media> audio - media тип
    <port> 52008 - порт по которому будет отправлен медиа поток 
    <proto> RTP/AVP - транспортный протокол. RTP с профилем Profile with Minimum Control
    <fmt>.. - 0, 8, 9, 18, 101 - номера payload type
m=audio 52008 RTP/AVP 0 8 9 18 101
    <mid> - идентификатор медиапотока
a=mid:0
    <rtpmap> - расшифровка payload type
a=rtpmap:0 PCMU/8000
    <rtcp-fb> - способ обратной связи для медиапотока - goog-remb
a=rtcp-fb:0 goog-remb
a=rtpmap:8 PCMA/8000
a=rtcp-fb:8 goog-remb
a=rtpmap:9 G722/8000
a=rtcp-fb:9 goog-remb
a=rtpmap:18 G729/8000
a=rtcp-fb:18 goog-remb
a=rtpmap:101 telephone-event/8000
a=rtcp-fb:101 goog-remb
    <fmtp> -0-16  указывает, что поддерживаются все DTMF сигналы от 0 до 16
a=fmtp:101 0-16
a=sendrecv
    <ssrc> - 1546063820 - идентификатор медиа-источника (для m-строки)
    <cname> - 76d7dd9d-3955-43ec-8d8c-b0581cfd2fde - "canonical name" (каноническое имя) источника
a=ssrc:1546063820 cname:76d7dd9d-3955-43ec-8d8c-b0581cfd2fde
 */


// Create a client user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
//играет роль клиента
var uac = new SIPClientUserAgent(sipTransport);
// CallTrying - для показания, что запрос отправлен и обрабатывается
uac.CallTrying += (u, resp) => Console.WriteLine($"" +
                                                 $"{u.CallDescriptor.To} " +
                                                 $"Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
// CallRinging - для показания, что запрос дошел до абонента и у него сейчас проигрывается звонок
// CallRinging когда sipResponse.Status == Ringing || sipResponse.Status == SessionProgress
// SessionProgress используется для определения параметров медиа вызываемой стороны
uac.CallRinging += async (u, resp) =>
{
    Console.WriteLine($"{u.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
    if (resp is { Status: SIPResponseStatusCodesEnum.SessionProgress, Body: not null })
    {
        //ParseSDPDescription - создает SDP экземпляр на основе resp.Body
        //SetRemoteDescription - устанавливает RemoteDescription для того, чтобы передавать данные через сессию
        var result = rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
        if (result == SetDescriptionResultEnum.OK)
        {
            await rtpSession.Start();
            Console.WriteLine($"Remote SDP set from in progress response. RTP session started.");
        }
    }
};
uac.CallFailed += (u, err, resp) =>
{
    Console.WriteLine($"Call attempt to {u.CallDescriptor.To} Failed: {err}");
    hasCallFailed = true;
};
uac.CallAnswered += async (iuac, resp) =>
{
    if (resp.Status == SIPResponseStatusCodesEnum.Ok)
    {
        Console.WriteLine($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");

        if (resp.Body != null)
        {
            var result = rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
            if (result == SetDescriptionResultEnum.OK)
            {
                await rtpSession.Start();
            }
            else
            {
                Console.WriteLine($"Failed to set remote description {result}.");
                uac.Hangup();
            }
        }
        else if (!rtpSession.IsStarted)
        {
            Console.WriteLine($"Failed to set get remote description in session progress or final response.");
            uac.Hangup();
        }
    }
    else
    {
        Console.WriteLine($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
    }
};

// The only incoming request that needs to be explicitly handled for this example is if the remote end hangs up the call.
sipTransport.SIPTransportRequestReceived += async (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
{
    if (sipRequest.Method == SIPMethodsEnum.BYE)
    {
        SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
        await sipTransport.SendResponseAsync(okResponse);

        if (uac.IsUACAnswered)
        {
            Console.WriteLine("Call was hungup by remote server.");
            isCallHungup = true;
            exitMre.Set();
        }
    }
};

// Start the thread that places the call.
var callDescriptor = new SIPCallDescriptor(
    SIPConstants.SIP_DEFAULT_USERNAME,
    null,
    callUri.ToString(),
    SIPConstants.SIP_DEFAULT_FROMURI,
    callUri.CanonicalAddress,
    null, null, null,
    SIPCallDirection.Out,
    SDP.SDP_MIME_CONTENTTYPE,
    offerSDP.ToString(),
    null);

var invite = uac.Call(callDescriptor, null);

// Ctrl-c will gracefully exit the call at any point.
Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    exitMre.Set();
};

// Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
exitMre.WaitOne();

Console.WriteLine("Exiting...");

rtpSession.Close(null);

if (!isCallHungup && uac != null)
{
    if (uac.IsUACAnswered)
    {
        Console.WriteLine($"Hanging up call to {uac.CallDescriptor.To}.");
        uac.Hangup();
    }
    else if (!hasCallFailed)
    {
        Console.WriteLine($"Cancelling call to {uac.CallDescriptor.To}.");
        uac.Cancel();
    }

    // Give the BYE or CANCEL request time to be transmitted.
    Console.WriteLine("Waiting 1s for call to clean up...");
    Task.Delay(1000).Wait();
}

if (sipTransport != null)
{
    Console.WriteLine("Shutting down SIP transport...");
    sipTransport.Shutdown();
}
