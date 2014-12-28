﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Parser.cs" company="Dan Barua">
//   (C) Dan Barua and contributors. Licensed under the Mozilla Public License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace NEventSocket.Sockets
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    using NEventSocket.FreeSwitch;
    using NEventSocket.Logging;
    using NEventSocket.Util;
    using NEventSocket.Util.ObjectPooling;

    /// <summary>
    /// A simple state-machine for parsing incoming EventSocket messages.
    /// </summary>
    public class Parser
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        // StringBuilder in .Net 4 uses a Linked List internally to avoid expensive reallocations. Faster but uses marginally more memory.
        private StringBuilder buffer = StringBuilderPool.Allocate();

        private char previous;

        private int? contentLength;

        private IDictionary<string, string> headers;

        /// <summary>
        /// Gets a value indicating whether parsing an incoming message has completed.
        /// </summary>
        public bool Completed { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the incoming message has a body.
        /// </summary>
        public bool HasBody
        {
            get
            {
                return this.contentLength.HasValue;
            }
        }

        /// <summary>
        /// Appends the given <see cref="char"/> to the message.
        /// </summary>
        /// <param name="next">The next <see cref="char"/> of the message.</param>
        /// <returns>The same instance of the <see cref="Parser"/>.</returns>
        public Parser Append(char next)
        {
            if (this.Completed)
            {
                return new Parser().Append(next);
            }

            this.buffer.Append(next);

            if (!this.HasBody)
            {
                // we're parsing the headers
                if (this.previous == '\n' && next == '\n')
                {
                    // \n\n denotes the end of the Headers
                    this.headers = this.buffer.ToString().ParseKeyValuePairs("\n", ": ");

                    if (this.headers.ContainsKey(HeaderNames.ContentLength))
                    {
                        this.contentLength = int.Parse(this.headers[HeaderNames.ContentLength]);

                        // start parsing the body content
                        this.buffer.Clear();

                        // allocate the buffer up front given that we now know the expected size
                        this.buffer.EnsureCapacity(this.contentLength.Value);
                    }
                    else
                    {
                        // end of message
                        this.Completed = true;
                    }
                }
                else
                {
                    this.previous = next;
                }
            }
            else
            {
                // if we've read the Content-Length amount of bytes then we're done
                this.Completed = this.buffer.Length == this.contentLength.GetValueOrDefault();
            }

            return this;
        }

        public Parser Append(string next)
        {
            var parser = this;

            foreach (var c in next)
            {
                parser = parser.Append(next);
            }

            return parser;
        }

        public BasicMessage ExtractMessage()
        {
            if (!this.Completed)
            {
                var errorMessage = "The message was not completely parsed.";

                if (this.HasBody)
                {
                    errorMessage += "expected a body with length {0}, got {1} instead.".Fmt(contentLength, buffer.Length);
                }

                throw new InvalidOperationException(errorMessage);
            }

            var result = this.HasBody ? new BasicMessage(this.headers, buffer.ToString()) : new BasicMessage(this.headers);
            StringBuilderPool.Free(buffer);
            buffer = null;
            return result;
        }
    }
}