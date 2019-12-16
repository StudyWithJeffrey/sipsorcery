## Call Hold and Transfer

The [Call Hold and Transfer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/CallHoldAndTransfer) program contains an example of how to place an established call on and off hold as well as initiate blind transfers.

### Call Hold

There are a number of different ways to put a SIP call on hold. This example uses SIP re-INVITE requests with the RTP flow attribute modified to indicate the call hold status.

The majority of the code in the example is to set up the initial the call. The interesting pieces of code as far as putting the call on hold arein the `ReinviteRequestReceived` method and in the `Task` handling the key presses ('h' is used to put the remote party on hold). Those two blocks are shown below.

````csharp
/// <summary>
/// Event handler for receiving a re-INVITE request on an established call.
/// In call requests can be used for multitude of different purposes. In this  
/// example program we're only concerned with re-INVITE requests being used 
/// to place a call on/off hold.
/// </summary>
/// <param name="uasTransaction">The user agent server invite transaction that
/// was created for the request. It needs to be used for sending responses 
/// to ensure reliable delivery.</param>
private static void ReinviteRequestReceived(UASInviteTransaction uasTransaction)
{
	SIPRequest reinviteRequest = uasTransaction.TransactionRequest;

	// Re-INVITEs can also be changing the RTP end point. We can update this each time.
	IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(reinviteRequest.Body);
	_remoteRtpEndPoint = dstRtpEndPoint;

	// If the RTP callfow attribute has changed it's most likely due to being placed on/off hold.
	SDP newSDP = SDP.ParseSDPDescription(reinviteRequest.Body);
	if (GetRTPStatusAttribute(newSDP) == RTP_ATTRIBUTE_SENDONLY)
	{
		Log.LogInformation("Remote call party has placed us on hold.");
		_holdStatus = HoldStatus.RemotePutOnHold;

		_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_RECVONLY);
		var okResponse = SIPTransport.GetResponse(reinviteRequest, SIPResponseStatusCodesEnum.Ok, null);
		okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
		okResponse.Body = _ourSDP.ToString();
		uasTransaction.SendFinalResponse(okResponse);
	}
	else if (GetRTPStatusAttribute(newSDP) == RTP_ATTRIBUTE_SENDRECV && _holdStatus != HoldStatus.None)
	{
		Log.LogInformation("Remote call party has taken us off hold.");
		_holdStatus = HoldStatus.None;

		_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDRECV);
		var okResponse = SIPTransport.GetResponse(reinviteRequest, SIPResponseStatusCodesEnum.Ok, null);
		okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
		okResponse.Body = _ourSDP.ToString();
		uasTransaction.SendFinalResponse(okResponse);
	}
	else
	{
		Log.LogWarning("Not sure what the remote call party wants us to do...");

		// We'll just reply Ok and hope eveything is good.
		var okResponse = SIPTransport.GetResponse(reinviteRequest, SIPResponseStatusCodesEnum.Ok, null);
		okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
		okResponse.Body = _ourSDP.ToString();
		uasTransaction.SendFinalResponse(okResponse);
	}
}
````

The task handling user key presses is shown below.

````csharp

// At this point the call has been initiated and everything will be handled in an event handler.
Task.Run(() =>
{
	try
	{
		while (!exitCts.Token.WaitHandle.WaitOne(0))
		{
			var keyProps = Console.ReadKey();
			if (keyProps.KeyChar == 'h')
			{
				// Place call on/off hold.
				if (userAgent.IsAnswered)
				{
					if (_holdStatus == HoldStatus.None)
					{
						Log.LogInformation("Placing the remote call party on hold.");
						_holdStatus = HoldStatus.WePutOnHold;
						_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDONLY);
						userAgent.SendReInviteRequest(_ourSDP);
					}
					else if (_holdStatus == HoldStatus.WePutOnHold)
					{
						Log.LogInformation("Removing the remote call party from hold.");
						_holdStatus = HoldStatus.None;
						_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDRECV);
						userAgent.SendReInviteRequest(_ourSDP);
					}
					else
					{
						Log.LogInformation("Sorry we're already on hold by the remote call party.");
					}
				}
			}
			else if (keyProps.KeyChar == 'q')
			{
				// Quit application.
				exitCts.Cancel();
			}
		}
	}
	catch (Exception excp)
	{
		SIPSorcery.Sys.Log.Logger.LogError($"Exception Key Press listener. {excp.Message}.");
	}
});
````

In the case of the `ReinviteRequestReceived` the remote party is placing the call on and off hold. In the second case handlng it's the example program putting the call on and off hold.

Each call hold is done by changing a single attribute on the SDP and sending it to the remote party via a re-INVITE request.

For example the original SDP sent to establish the call will look something like the payload below. The important attribute is the last one `a=sendrecv`.

````
v=0
o=- 49809 0 IN IP4 192.168.11.50
s=sipsorcery
c=IN IP4 192.168.11.50
t=0 0
m=audio 48000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
````

The remote call party can be put on hold using.

````
v=0
o=- 49735 0 IN IP4 192.168.11.50
s=sipsorcery
c=IN IP4 192.168.11.50
t=0 0
m=audio 48000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendonly
````

They will respond with an SDP payload along the lines of what's shown below. Again the line changing is the last attribute `a=recvonly`.

````
v=0
o=- 1667266393 3 IN IP4 192.168.11.50
s=Bria 4 release 4.8.1 stamp 84929
c=IN IP4 192.168.11.50
t=0 0
m=audio 59228 RTP/AVP 0
a=recvonly
````

To take the call off hold it's a matter of setting the RTP flow attribute back to `sendrecv` as shown in the original SDP.

### Blind Transfer

Another difference is this example uses the `SIPUserAgent` instead of the `SIPClientUserAgent`. The `SIPUserAgent` is a combination of the client and server user agents and also understands in dialog requests (things like call hold and transfer). By contrast the `SIPClientUserAgent` considers its job done once the call attempt it answered or rejected.

The type of transfer used is a `Blind Transfer`. The remote call party is requested to place a new call directly. An `Attended Transfer` is where the transferee places the call, does some talking (assumedly) and then bridges the two parties together.

The diagram below represents a typical call flow when using this program. The `softphone` calls the `sipsorcery` program. After answering the `sipsorcery` program transfers the `softphone` by requesting it to place a call to the `asterisk` server.

The example uses REFER requests as specified in [RFC3515](https://tools.ietf.org/html/rfc3515).

![image](images/xfer_callflow.png)


The key piece of code is:

````csharp
// At this point the call is established. We'll wait for a few seconds and then transfer.
Task.Delay(DELAY_UNTIL_TRANSFER_MILLISECONDS).Wait();

SIPRequest referRequest = GetReferRequest(uac, SIPURI.ParseSIPURI(TRANSFER_DESTINATION_SIP_URI));
SIPNonInviteTransaction referTx = sipTransport.CreateNonInviteTransaction(referRequest, referRequest.RemoteSIPEndPoint, referRequest.LocalSIPEndPoint, null);

referTx.NonInviteTransactionFinalResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) =>
{
	if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.REFER && sipResponse.Status == SIPResponseStatusCodesEnum.Accepted)
	{
		Log.LogInformation("Call transfer was accepted by remote server.");
		isCallHungup = true;
		rtpCts.Cancel();
	}
};

referTx.SendReliableRequest();

// At this point the call transfer has been initiated and everything will be handled in an event handler or on the RTP
// receive task. The code below is to gracefully exit.
````

The method used to construct the REFER request is:

````csharp
private static SIPRequest GetReferRequest(SIPClientUserAgent uac, SIPURI referToUri)
{
	SIPDialogue sipDialogue = uac.SIPDialogue;

	SIPRequest referRequest = new SIPRequest(SIPMethodsEnum.REFER, sipDialogue.RemoteTarget);
	SIPFromHeader referFromHeader = SIPFromHeader.ParseFromHeader(sipDialogue.LocalUserField.ToString());
	SIPToHeader referToHeader = SIPToHeader.ParseToHeader(sipDialogue.RemoteUserField.ToString());
	int cseq = sipDialogue.CSeq + 1;
	sipDialogue.CSeq++;

	SIPHeader referHeader = new SIPHeader(referFromHeader, referToHeader, cseq, sipDialogue.CallId);
	referHeader.CSeqMethod = SIPMethodsEnum.REFER;
	referRequest.Header = referHeader;
	referRequest.Header.Routes = sipDialogue.RouteSet;
	referRequest.Header.ProxySendFrom = sipDialogue.ProxySendFrom;

	SIPViaHeader viaHeader = new SIPViaHeader(uac.ServerTransaction.LocalSIPEndPoint, CallProperties.CreateBranchId());
	referRequest.Header.Vias.PushViaHeader(viaHeader);

	referRequest.Header.ReferTo = referToUri.ToString();
	referRequest.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, uac.ServerTransaction.TransactionRequest.Header.Contact.First().ContactURI) };
	referRequest.RemoteSIPEndPoint = uac.ServerTransaction.RemoteEndPoint;

	return referRequest;
}
````

Within that method the most important part is the setting of the `ReferTo` header. It's what determines where the remote user agent will is being asked to place a new call to.

**It's important to note that a REFER request "asks" the remote user agent to try placing a new call to the SIP URI specified. There is no guarantee it will or that the call will succeed. It's important to deal with cases where the REFER request is rejected.**

Below is the console output of a successful call transfer using the exmaple code and a local Asterisk\FreePBX server.

````cmd
C:\Dev\sipsorcery\sipsorcery-core\examples\CallTransfer>dotnet run
SIPSorcery client user agent example.
Press ctrl-c to exit.
[21:41:05 INF] Call destination sip:100@192.168.11.48.
[21:41:05 DBG] SIPUDPChannel listener created 0.0.0.0:6060.
[21:41:05 DBG] SIPUDPChannel socket on udp:0.0.0.0:6060 listening started.
[21:41:05 DBG] Request sent: udp:0.0.0.0:6060->udp:192.168.11.48:5060
[21:41:05 DBG] OPTIONS sip:100@192.168.11.48 SIP/2.0
Via: SIP/2.0/UDP 0.0.0.0:6060;branch=z9hG4bK606f5c765d9d430a9ac6a7325cb8fe0c;rport
To: <sip:100@192.168.11.48>
From: <sip:0.0.0.0:6060>;tag=HYSDCANLXD
Call-ID: db3bbb46071c41968c623a3d7ab01489
CSeq: 1 OPTIONS
Contact: <sip:0.0.0.0:6060>
Max-Forwards: 70
Allow: ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, REFER, REGISTER, SUBSCRIBE
Content-Length: 0


[21:41:05 DBG] Response received: udp:0.0.0.0:6060<-udp:192.168.11.48:5060
[21:41:05 DBG] SIP/2.0 200 OK
Via: SIP/2.0/UDP 0.0.0.0:6060;rport=6060;received=192.168.11.50;branch=z9hG4bK606f5c765d9d430a9ac6a7325cb8fe0c
To: <sip:100@192.168.11.48>;tag=z9hG4bK606f5c765d9d430a9ac6a7325cb8fe0c
From: <sip:0.0.0.0>;tag=HYSDCANLXD
Call-ID: db3bbb46071c41968c623a3d7ab01489
CSeq: 1 OPTIONS
Accept: application/dialog-info+xml, application/xpidf+xml, application/cpim-pidf+xml, application/simple-message-summary, application/pidf+xml, application/pidf+xml, application/dialog-info+xml, application/simple-message-summary, application/sdp, message/sipfrag;version=2.0
Accept-Encoding: text/plain
Accept-Language: en
Allow: OPTIONS, REGISTER, SUBSCRIBE, NOTIFY, PUBLISH, INVITE, ACK, BYE, CANCEL, UPDATE, PRACK, MESSAGE, REFER
Supported: 100rel, timer, replaces, norefersub
Content-Length: 0
Server: FPBX-15.0.16.20(16.4.1)


[21:41:05 DBG] Response received udp:0.0.0.0:6060<-udp:192.168.11.48:5060: OPTIONS 200 OK
[21:41:05 DBG] Successfully bound RTP socket 192.168.11.50:49000.
[21:41:05 DBG] Listening on RTP socket 192.168.11.50:49000.
[21:41:05 DBG] Attempting to resolve 192.168.11.48.
[21:41:05 DBG] Switching to sip:100@192.168.11.48:5060 via udp:192.168.11.48:5060.
[21:41:05 DBG] SDP on UAC could not be mangled, using original RTP socket of 192.168.11.50:49000.
[21:41:05 DBG] Send Request reliable udp:0.0.0.0:6060->udp:192.168.11.48:5060: INVITE sip:100@192.168.11.48 SIP/2.0
[21:41:05 DBG] Request sent: udp:0.0.0.0:6060->udp:192.168.11.48:5060
[21:41:05 DBG] INVITE sip:100@192.168.11.48 SIP/2.0
Via: SIP/2.0/UDP 0.0.0.0:6060;branch=z9hG4bK76c11432449d48e6805fb89adb686752;rport
To: <sip:100@192.168.11.48>
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 1 INVITE
Contact: <sip:Anonymous@pcdodo:6060>
Max-Forwards: 70
User-Agent: www.sipsorcery.com
Supported: 100rel
Content-Length: 143
Content-Type: application/sdp

v=0
o=- 49124 0 IN IP4 192.168.11.50
s=sipsorcery
c=IN IP4 192.168.11.50
t=0 0
m=audio 49000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv

[21:41:06 DBG] Response received: udp:0.0.0.0:6060<-udp:192.168.11.48:5060
[21:41:06 DBG] SIP/2.0 100 Trying
Via: SIP/2.0/UDP 0.0.0.0:6060;rport=6060;received=192.168.11.50;branch=z9hG4bK76c11432449d48e6805fb89adb686752
To: <sip:100@192.168.11.48>
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 1 INVITE
Content-Length: 0
Server: FPBX-15.0.16.20(16.4.1)


[21:41:06 DBG] Received Response udp:0.0.0.0:6060<-udp:192.168.11.48:5060: INVITE 100 Trying
[21:41:06 DBG] Information response 100 Trying for sip:100@192.168.11.48.
[21:41:06 INF] sip:100@192.168.11.48 Trying: 100 Trying.
[21:41:06 DBG] SIP/2.0 100 Trying
Via: SIP/2.0/UDP 0.0.0.0:6060;rport=6060;received=192.168.11.50;branch=z9hG4bK76c11432449d48e6805fb89adb686752
To: <sip:100@192.168.11.48>
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 1 INVITE
Content-Length: 0
Server: FPBX-15.0.16.20(16.4.1)


[21:41:06 DBG] Response received: udp:0.0.0.0:6060<-udp:192.168.11.48:5060
[21:41:06 DBG] SIP/2.0 180 Ringing
Via: SIP/2.0/UDP 0.0.0.0:6060;rport=6060;received=192.168.11.50;branch=z9hG4bK76c11432449d48e6805fb89adb686752
To: <sip:100@192.168.11.48>;tag=b2b886e3-84f5-4bbc-8c05-74b115d7edcb
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 1 INVITE
Contact: <sip:192.168.11.48:5060>
Allow: OPTIONS, REGISTER, SUBSCRIBE, NOTIFY, PUBLISH, INVITE, ACK, BYE, CANCEL, UPDATE, PRACK, MESSAGE, REFER
Content-Length: 0
Server: FPBX-15.0.16.20(16.4.1)


[21:41:06 DBG] Received Response udp:0.0.0.0:6060<-udp:192.168.11.48:5060: INVITE 180 Ringing
[21:41:06 DBG] Information response 180 Ringing for sip:100@192.168.11.48.
[21:41:06 INF] sip:100@192.168.11.48 Ringing: 180 Ringing.
[21:41:06 DBG] Response received: udp:0.0.0.0:6060<-udp:192.168.11.48:5060
[21:41:06 DBG] SIP/2.0 180 Ringing
Via: SIP/2.0/UDP 0.0.0.0:6060;rport=6060;received=192.168.11.50;branch=z9hG4bK76c11432449d48e6805fb89adb686752
To: <sip:100@192.168.11.48>;tag=b2b886e3-84f5-4bbc-8c05-74b115d7edcb
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 1 INVITE
Contact: <sip:192.168.11.48:5060>
Allow: OPTIONS, REGISTER, SUBSCRIBE, NOTIFY, PUBLISH, INVITE, ACK, BYE, CANCEL, UPDATE, PRACK, MESSAGE, REFER
Content-Length: 0
Server: FPBX-15.0.16.20(16.4.1)


[21:41:06 DBG] Received Response udp:0.0.0.0:6060<-udp:192.168.11.48:5060: INVITE 180 Ringing
[21:41:06 DBG] Information response 180 Ringing for sip:100@192.168.11.48.
[21:41:06 INF] sip:100@192.168.11.48 Ringing: 180 Ringing.
[21:41:10 DBG] Response received: udp:0.0.0.0:6060<-udp:192.168.11.48:5060
[21:41:10 DBG] Initial RTP packet recieved from 192.168.11.48:15300.
[21:41:10 DBG] SIP/2.0 200 OK
Via: SIP/2.0/UDP 0.0.0.0:6060;rport=6060;received=192.168.11.50;branch=z9hG4bK76c11432449d48e6805fb89adb686752
To: <sip:100@192.168.11.48>;tag=b2b886e3-84f5-4bbc-8c05-74b115d7edcb
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 1 INVITE
Contact: <sip:192.168.11.48:5060>
Allow: OPTIONS, REGISTER, SUBSCRIBE, NOTIFY, PUBLISH, INVITE, ACK, BYE, CANCEL, UPDATE, PRACK, MESSAGE, REFER
Supported: 100rel, timer, replaces, norefersub
Content-Length: 169
Content-Type: application/sdp
Server: FPBX-15.0.16.20(16.4.1)

v=0
o=- 49124 2 IN IP4 192.168.11.48
s=Asterisk
c=IN IP4 192.168.11.48
t=0 0
m=audio 15300 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=ptime:20
a=maxptime:150
a=sendrecv

[21:41:10 DBG] Received Response udp:0.0.0.0:6060<-udp:192.168.11.48:5060: INVITE 200 OK
[21:41:10 DBG] Send Request udp:0.0.0.0:6060->udp:192.168.11.48:5060: ACK sip:192.168.11.48:5060 SIP/2.0
[21:41:10 DBG] Request sent: udp:0.0.0.0:6060->udp:192.168.11.48:5060
[21:41:10 DBG] ACK sip:192.168.11.48:5060 SIP/2.0
Via: SIP/2.0/UDP 0.0.0.0:6060;branch=z9hG4bK26032492c2af40f9963efa80577642e9;rport
To: <sip:100@192.168.11.48>;tag=b2b886e3-84f5-4bbc-8c05-74b115d7edcb
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 1 ACK
Max-Forwards: 70
Content-Length: 0


[21:41:10 DBG] Response 200 OK for sip:100@192.168.11.48.
[21:41:10 DBG] SDP on UAC response could not be mangled, RTP socket 192.168.11.48:15300.
[21:41:11 INF] sip:100@192.168.11.48 Answered: 200 OK.
[21:41:11 DBG] Remote RTP socket 192.168.11.48:15300.
[21:41:16 DBG] Request sent: udp:0.0.0.0:6060->udp:192.168.11.48:5060
[21:41:16 DBG] REFER sip:192.168.11.48:5060 SIP/2.0
Via: SIP/2.0/UDP 0.0.0.0:6060;branch=z9hG4bKcb147fd53f5149678c0c52908f2f5d9c;rport
To: <sip:100@192.168.11.48>;tag=b2b886e3-84f5-4bbc-8c05-74b115d7edcb
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 2 REFER
Contact: <sip:Anonymous@pcdodo:6060>
Max-Forwards: 70
Content-Length: 0
Refer-To: sip:*60@192.168.11.48


[21:41:16 DBG] Response received: udp:0.0.0.0:6060<-udp:192.168.11.48:5060
[21:41:16 DBG] SIP/2.0 202 Accepted
Via: SIP/2.0/UDP 0.0.0.0:6060;rport=6060;received=192.168.11.50;branch=z9hG4bKcb147fd53f5149678c0c52908f2f5d9c
To: <sip:100@192.168.11.48>;tag=b2b886e3-84f5-4bbc-8c05-74b115d7edcb
From: <sip:thisis@anonymous.invalid>;tag=FEDPDHKAQK
Call-ID: a5b30d51d07d42dfb39b4fe8d02f6f6a
CSeq: 2 REFER
Contact: <sip:192.168.11.48:5060>
Expires: 600
Allow: OPTIONS, REGISTER, SUBSCRIBE, NOTIFY, PUBLISH, INVITE, ACK, BYE, CANCEL, UPDATE, PRACK, MESSAGE, REFER
Supported: 100rel, timer, replaces, norefersub
Content-Length: 0
Server: FPBX-15.0.16.20(16.4.1)


[21:41:16 INF] Call transfer was accepted by remote server.
[21:41:16 INF] Exiting...
[21:41:16 DBG] Initialising DNS resolver with operating system DNS server entries.
[21:41:16 DBG] DNS server 8.8.8.8:53
[21:41:16 DBG] DNS server 192.168.0.1:53
[21:41:16 DBG] DNSManager Stopping.
[21:41:16 INF] Shutting down SIP transport...
[21:41:16 DBG] Closing SIP UDP Channel udp:0.0.0.0:6060.
[21:41:16 DBG] SIPUDPChannel socket on udp:0.0.0.0:6060 listening halted.
````