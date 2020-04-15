/*
 * Copyright (C) 2011 uhttpsharp project - http://github.com/raistlinthewiz/uhttpsharp
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.

 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.

 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using uhttpsharp.Clients;
using uhttpsharp.Headers;
using uhttpsharp.RequestProviders;

namespace uhttpsharp {
    internal sealed class HttpClientHandler {
        private const string CrLf = "\r\n";
        private static readonly byte[] CrLfBuffer = Encoding.UTF8.GetBytes(CrLf);

        private readonly ILogger _logger;
        private readonly EndPoint _remoteEndPoint;
        private readonly Func<IHttpContext, Task> _requestHandler;
        private readonly IHttpRequestProvider _requestProvider;
        private readonly Stream _stream;

        public IClient Client { get; }

        public DateTime LastOperationTime { get; private set; }

        public HttpClientHandler(IClient client, Func<IHttpContext, Task> requestHandler, IHttpRequestProvider requestProvider, ILogger logger = null) {
            _remoteEndPoint = client.RemoteEndPoint;
            Client = client;
            _requestHandler = requestHandler;
            _requestProvider = requestProvider;
            _logger = logger;

            _stream = new BufferedStream(Client.Stream, 8192);

            _logger?.Debug($"Got Client {_remoteEndPoint}");

            Task.Factory.StartNew(Process);

            UpdateLastOperationTime();
        }

        private async void Process() {
            try {
                while (Client.Connected) {
                    // TODO : Configuration.
                    var limitedStream = new NotFlushingStream(new LimitedStream(_stream, 1024 * 1024, 1024 * 1024));
                    var streamReader = new StreamReader(limitedStream);

                    var request = await _requestProvider.Provide(streamReader).ConfigureAwait(false);

                    if (request != null) {
                        UpdateLastOperationTime();

                        var context = new HttpContext(request, Client.RemoteEndPoint);

                        _logger?.Debug($"{Client.RemoteEndPoint} : Got request {request.Uri}");

                        await _requestHandler(context).ConfigureAwait(false);

                        if (context.Response != null) {
                            var streamWriter = new StreamWriter(limitedStream) {AutoFlush = false};

                            await WriteResponse(context, streamWriter).ConfigureAwait(false);
                            await limitedStream.ExplicitFlushAsync().ConfigureAwait(false);

                            if (!request.Headers.KeepAliveConnection() || context.Response.CloseConnection) Client.Close();
                        }

                        UpdateLastOperationTime();
                    } else {
                        Client.Close();
                    }
                }
            } catch (Exception e) {
                // Hate people who make bad calls.
                _logger?.Warn($"Error while serving : {_remoteEndPoint}", e);
                Client.Close();
            }

            _logger?.Debug($"Lost Client {_remoteEndPoint}");
        }

        private async Task WriteResponse(HttpContext context, StreamWriter writer) {
            var response = context.Response;
            var request = context.Request;

            // Headers
            await writer.WriteLineAsync(string.Format("HTTP/1.1 {0} {1}",
                    (int) response.ResponseCode,
                    response.ResponseCode))
                .ConfigureAwait(false);

            foreach (var header in response.Headers) await writer.WriteLineAsync(string.Format("{0}: {1}", header.Key, header.Value)).ConfigureAwait(false);

            // Cookies
            if (context.Cookies.Touched)
                await writer.WriteAsync(context.Cookies.ToCookieData())
                    .ConfigureAwait(false);

            // Empty Line
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);

            // Body
            await response.WriteBody(writer).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }

        public void ForceClose() {
            Client.Close();
        }

        private void UpdateLastOperationTime() {
            LastOperationTime = DateTime.Now;
        }
    }

    internal class NotFlushingStream : Stream {
        private readonly Stream _child;

        public override bool CanRead => _child.CanRead;

        public override bool CanSeek => _child.CanSeek;

        public override bool CanWrite => _child.CanWrite;

        public override long Length => _child.Length;

        public override long Position {
            get => _child.Position;
            set => _child.Position = value;
        }

        public override int ReadTimeout {
            get => _child.ReadTimeout;
            set => _child.ReadTimeout = value;
        }

        public override int WriteTimeout {
            get => _child.WriteTimeout;
            set => _child.WriteTimeout = value;
        }

        public NotFlushingStream(Stream child) {
            _child = child;
        }

        public void ExplicitFlush() {
            _child.Flush();
        }

        public Task ExplicitFlushAsync() {
            return _child.FlushAsync();
        }

        public override void Flush() {
            // _child.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            return _child.Seek(offset, origin);
        }

        public override void SetLength(long value) {
            _child.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count) {
            return _child.Read(buffer, offset, count);
        }

        public override int ReadByte() {
            return _child.ReadByte();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            _child.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value) {
            _child.WriteByte(value);
        }
    }

    public static class RequestHandlersAggregateExtensions {
        public static Func<IHttpContext, Task> Aggregate(this IList<IHttpRequestHandler> handlers) {
            return handlers.Aggregate(0);
        }

        private static Func<IHttpContext, Task> Aggregate(this IList<IHttpRequestHandler> handlers, int index) {
            if (index == handlers.Count) return null;

            var currentHandler = handlers[index];
            var nextHandler = handlers.Aggregate(index + 1);

            return context => currentHandler.Handle(context, () => nextHandler(context));
        }
    }
}