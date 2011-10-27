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
using System.Reflection;

namespace uhttpsharp
{
    internal sealed class HttpRouter
    {
        private readonly Dictionary<string, HttpRequestHandler> _handlers = new Dictionary<string, HttpRequestHandler>();

        public HttpRouter()
        {
            RegisterHandlers();
        }

        private HttpResponse DefaultError()
        {
            return HttpResponse.CreateWithMessage(HttpResponseCode.NotFound, "Not Found");
        }
        private HttpResponse DefaultIndex()
        {
            return HttpResponse.CreateWithMessage(HttpResponseCode.Ok, "Welcome to uhttpsharp!");
        }
        public HttpResponse Route(HttpRequest request)
        {
            var function = request.Parameters.Function;
            return
                RouteToFunction(request, function) ??
                RouteToFunction(request, "*") ??
                (string.IsNullOrEmpty(function) ? (RouteToFunction(request, "") ?? DefaultIndex()) : null) ??
                RouteToFunction(request, "404") ??
                DefaultError();
        }
        private HttpResponse RouteToFunction(HttpRequest request, string function)
        {
            HttpRequestHandler handler;
            if (_handlers.TryGetValue(function, out handler))
                return handler.Handle(request);
            return null;
        }
        private void RegisterHandlers()
        {
            foreach (var t in Assembly.GetEntryAssembly().GetTypes())
            {
                if (t.IsSubclassOf(typeof(HttpRequestHandler)))
                {
                    try
                    {
                        var attributes = t.GetCustomAttributes(typeof(HttpRequestHandlerAttributes), true);
                        if (attributes.Length > 0)
                        {
                            var handler = (HttpRequestHandler)Activator.CreateInstance(t);
                            _handlers.Add(((HttpRequestHandlerAttributes)attributes[0]).Function, handler);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(string.Format("Exception during activating the IHttpRequestHandler: {0} - {1}", t, e));
                    }
                }
            }
        }
    }
}