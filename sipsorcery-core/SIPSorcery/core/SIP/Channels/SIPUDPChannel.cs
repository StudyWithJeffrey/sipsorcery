//-----------------------------------------------------------------------------
// Filename: SIPUDPChannel.cs
//
// Description: SIP transport for UDP.
// 
// History:
// 17 Oct 2005	Aaron Clauson	Created.
// 14 Oct 2019  Aaron Clauson   Added IPv6 support.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2005-2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPUDPChannel : SIPChannel
    {
        private const string THREAD_NAME = "sipchanneludp-";

        // Channel sockets.
        private UdpClient m_sipConn = null;

        public SIPUDPChannel(IPEndPoint endPoint)
        {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, endPoint);
            Initialise();
        }

        public SIPUDPChannel(IPAddress listenAddress, int listenPort) : this(new IPEndPoint(listenAddress, listenPort))
        { }

        private void Initialise()
        {
            try
            {
                m_sipConn = new UdpClient(m_localSIPEndPoint.GetIPEndPoint());

                if (m_localSIPEndPoint.Port == 0)
                {
                    m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, (IPEndPoint)m_sipConn.Client.LocalEndPoint);
                }

                Thread listenThread = new Thread(new ThreadStart(Listen));
                listenThread.Name = THREAD_NAME + Crypto.GetRandomString(4);
                listenThread.Start();

                logger.LogDebug("SIPUDPChannel listener created " + m_localSIPEndPoint.GetIPEndPoint() + ".");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPUDPChannel Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void Dispose(bool disposing)
        {
            try
            {
                this.Close();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception Disposing SIPUDPChannel. " + excp.Message);
            }
        }

        private void Listen()
        {
            try
            {
                byte[] buffer = null;

                logger.LogDebug("SIPUDPChannel socket on " + m_localSIPEndPoint.ToString() + " listening started.");

                while (!Closed)
                {
                    IPEndPoint inEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    try
                    {
                        buffer = m_sipConn.Receive(ref inEndPoint);
                    }
                    catch (SocketException)
                    {
                        // ToDo. Pretty sure these exceptions get thrown when an ICMP message comes back indicating there is no listening
                        // socket on the other end. It would be nice to be able to relate that back to the socket that the data was sent to
                        // so that we know to stop sending.
                        //logger.LogWarning("SocketException SIPUDPChannel Receive (" + sockExcp.ErrorCode + "). " + sockExcp.Message);

                        //inEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Any, 0));
                        continue;
                    }
                    catch (Exception listenExcp)
                    {
                        logger.LogError("Exception listening on SIPUDPChannel. " + listenExcp.Message);
                        inEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        continue;
                    }

                    if (buffer == null || buffer.Length == 0)
                    {
                        // No need to care about zero byte packets.
                        //string remoteEndPoint = (inEndPoint != null) ? inEndPoint.ToString() : "could not determine";
                        //logger.LogError("Zero bytes received on SIPUDPChannel " + m_localSIPEndPoint.ToString() + ".");
                    }
                    else
                    {
                        SIPMessageReceived?.Invoke(this, new SIPEndPoint(SIPProtocolsEnum.udp, inEndPoint), buffer);
                    }
                }

                logger.LogDebug("SIPUDPChannel socket on " + m_localSIPEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPUDPChannel Listen. " + excp.Message);
                //throw excp;
            }
        }

        public override void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public override void Send(IPEndPoint destinationEndPoint, byte[] buffer)
        {
            try
            {
                if (destinationEndPoint == null)
                {
                    throw new ApplicationException("An empty destination was specified to Send in SIPUDPChannel.");
                }
                else
                {
                    m_sipConn.Send(buffer, buffer.Length, destinationEndPoint);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception (" + excp.GetType().ToString() + ") SIPUDPChannel Send (sendto=>" + IPSocket.GetSocketString(destinationEndPoint) + "). " + excp.Message);
                throw excp;
            }
        }

        public override async Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer)
        {
            if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to Send in SIPUDPChannel.");
            }
            else if(buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPUDPChannel.");
            }

            try
            {
                int bytesSent = await m_sipConn.SendAsync(buffer, buffer.Length, dstEndPoint);
                return (bytesSent > 0) ? SocketError.Success : SocketError.ConnectionReset;
            }
            catch(SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
            }
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        public override Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        public override bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            throw new NotSupportedException("The SIP UDP channel does not support connections.");
        }

        protected override Dictionary<string, SIPStreamConnection> GetConnectionsList()
        {
            throw new NotSupportedException("The SIP UDP channel does not support connections.");
        }

        public override void Close()
        {
            try
            {
                logger.LogDebug("Closing SIP UDP Channel " + SIPChannelEndPoint + ".");

                Closed = true;
                m_sipConn.Close();
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception SIPUDPChannel Close. " + excp.Message);
            }
        }

        public override void Dispose()
        {
            this.Close();
        }
    }
}
