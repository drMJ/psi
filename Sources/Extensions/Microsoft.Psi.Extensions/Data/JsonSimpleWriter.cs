﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

namespace Microsoft.Psi.Extensions.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Microsoft.Psi.Serialization;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Represents a simple writer for JSON data stores.
    /// </summary>
    public class JsonSimpleWriter : ISimpleWriter, IDisposable
    {
        private readonly Dictionary<int, Func<(bool hasData, JToken data, Envelope envelope)>> outputs = new Dictionary<int, Func<(bool hasData, JToken data, Envelope envelope)>>();
        private readonly string dataSchemaString;
        private readonly string extension;
        private readonly IDictionary<Uri, string> preloadSchemas;

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonSimpleWriter"/> class.
        /// </summary>
        /// <param name="name">The name of the application that generated the persisted files, or the root name of the files</param>
        /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store</param>
        /// <param name="dataSchemaString">JSON schema used to validate data stream.</param>
        /// <param name="createSubdirectory">If true, a numbered subdirectory is created for this store</param>
        /// <param name="extension">The extension for the underlying file.</param>
        /// <param name="preloadSchemas">Dictionary of URis to JSON schemas to preload before validating any JSON. Would likely include schemas references by the catalog and data schemas.</param>
        public JsonSimpleWriter(string name, string path, string dataSchemaString = JsonStoreBase.DataSchemaString, bool createSubdirectory = true, string extension = JsonStoreBase.DefaultExtension, IDictionary<Uri, string> preloadSchemas = null)
            : this(dataSchemaString, extension, preloadSchemas)
        {
            this.CreateStore(name, path, createSubdirectory);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonSimpleWriter"/> class.
        /// </summary>
        /// <param name="dataSchemaString">JSON schema used to validate data stream.</param>
        /// <param name="extension">The extension for the underlying file.</param>
        /// <param name="preloadSchemas">Dictionary of URis to JSON schemas to preload before validating any JSON. Would likely include schemas references by the catalog and data schemas.</param>
        public JsonSimpleWriter(string dataSchemaString = JsonStoreBase.DataSchemaString, string extension = JsonStoreBase.DefaultExtension, IDictionary<Uri, string> preloadSchemas = null)
        {
            this.dataSchemaString = dataSchemaString;
            this.extension = extension;
            this.preloadSchemas = preloadSchemas;
        }

        /// <inheritdoc />
        public string Name => this.Writer?.Name;

        /// <inheritdoc />
        public string Path => this.Writer?.Path;

        /// <summary>
        /// Gets or sets the underlying store writer.
        /// </summary>
        protected JsonStoreWriter Writer { get; set; }

        /// <inheritdoc />
        public virtual void CreateStore(string name, string path, bool createSubdirectory = true, KnownSerializers serializers = null)
        {
            if (serializers != null)
            {
                throw new ArgumentException("Serializers are not used by JsonSimpleWriter and must be null.", nameof(serializers));
            }

            this.Writer = new JsonStoreWriter(name, path, this.dataSchemaString, createSubdirectory, this.extension, this.preloadSchemas);
        }

        /// <inheritdoc />
        public void CreateStream<TData>(IStreamMetadata metadata, IEnumerable<Message<TData>> source)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (!(metadata is JsonStreamMetadata))
            {
                throw new ArgumentException($"Metadata must be of type '{nameof(JsonStreamMetadata)}'.", nameof(metadata));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var streamMetadata = this.Writer.OpenStream(metadata as JsonStreamMetadata);
            var enumerator = source.GetEnumerator();
            this.outputs[streamMetadata.Id] = () =>
            {
                bool hasData = enumerator.MoveNext();
                JToken data = null;
                Envelope envelope = default(Envelope);

                if (hasData)
                {
                    Message<TData> message = enumerator.Current;
                    data = JToken.FromObject(message.Data);
                    envelope = message.Envelope;
                }

                return (hasData, data, envelope);
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Writer?.Dispose();
        }

        /// <inheritdoc />
        public void WriteAll(ReplayDescriptor descriptor, CancellationToken cancelationToken = default(CancellationToken))
        {
            List<Func<(bool hasData, JToken data, Envelope envelope)>> doneStreamWriters = new List<Func<(bool hasData, JToken data, Envelope envelope)>>();
            var streamWriters = this.outputs.Values.ToList();
            while (streamWriters.Any())
            {
                foreach (var streamWriter in streamWriters)
                {
                    var(hasData, data, envelope) = streamWriter();
                    if (hasData)
                    {
                        this.Writer.Write(data, envelope);
                    }
                    else
                    {
                        doneStreamWriters.Add(streamWriter);
                    }
                }

                foreach (var doneStreamWriter in doneStreamWriters)
                {
                    streamWriters.Remove(doneStreamWriter);
                }

                doneStreamWriters.Clear();
            }
        }
    }
}
