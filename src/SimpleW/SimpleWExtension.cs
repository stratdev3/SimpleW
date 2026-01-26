using System.Buffers;
using System.Net;
using System.Text;
using SimpleW.Parsers;
using SimpleW.Security;


namespace SimpleW {

    /// <summary>
    /// SimpleW Extension helpers methods
    /// </summary>
    public static class SimpleWExtension {

        #region jwt

        /// <summary>
        /// CreateJwt
        /// </summary>
        /// <param name="session"></param>
        /// <param name="standard"></param>
        /// <param name="customClaims"></param>
        /// <returns></returns>
        public static string CreateJwt(this HttpSession session, JwtTokenPayload standard, IReadOnlyDictionary<string, object?> customClaims) {
            return Jwt.EncodeHs256(session.JsonEngine, standard, customClaims, session.Server.Options.JwtOptions!.Key);
        }

        /// <summary>
        /// ValidateJwt
        /// </summary>
        /// <param name="session"></param>
        /// <param name="token"></param>
        /// <param name="jwt"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public static bool ValidateJwt(this HttpSession session, string token, out JwtToken? jwt, out JwtError error) {
            return Jwt.TryDecodeAndValidate(session.JsonEngine, token, session.Server.Options.JwtOptions!, out jwt, out error);
        }

        #endregion jwt

        #region body

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <param name="jsonEngine">the json library to handle serialization/deserialization</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMap<TModel>(this HttpRequest request, TModel model, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null, IJsonEngine? jsonEngine = null) {
            string contentType = request.Headers.ContentType ?? "";
            string body = request.BodyString;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }

            // use default if null
            jsonEngine ??= request.JsonEngine;
            if (jsonEngine == null) {
                throw new Exception("BodyMap cannot use a null jsonEngine");
            }

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"multipart/form-data contentType must be parsed with BodyMultipart() or BodyMultipartStream() methods");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, object?> kv = request.BodyForm();
                body = jsonEngine.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {
                return JsonMap(body, model, jsonEngine, includeProperties, excludeProperties);
            }

            return true;
        }

        /// <summary>
        /// Update the anonymous model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Anonymous Model instance to populate.</param>
        /// <param name="jsonEngine">the json library to handle serialization/deserialization</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMapAnonymous<TModel>(this HttpRequest request, ref TModel model, IJsonEngine? jsonEngine = null) {
            string contentType = request.Headers.ContentType ?? "";
            string body = request.BodyString;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }

            // use default if null
            jsonEngine ??= request.JsonEngine;
            if (jsonEngine == null) {
                throw new Exception("BodyMapAnonymous cannot use a null jsonEngine");
            }

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new Exception($"multipart/form-data contentType must be parsed with BodyFile() method");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, object?> kv = request.BodyForm();
                body = jsonEngine.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {
                // deserialize AnonymousType
                model = jsonEngine.DeserializeAnonymous(body, model);
            }

            return true;
        }

        /// <summary>
        /// Parse application/x-www-form-urlencoded request body readonlysequence byte.
        /// - supports repeated keys => List&lt;string?&gt;
        /// - trims the trailing [] convention (key[]=a&amp;key[]=b)
        /// - decodes + and %xx using UTF-8
        /// </summary>
        /// <param name="request"></param>
        public static Dictionary<string, object?> BodyForm(this HttpRequest request) {
            Dictionary<string, object?> result = new(StringComparer.OrdinalIgnoreCase);

            ReadOnlySequence<byte> body = request.Body;
            if (body.IsEmpty) {
                return result;
            }

            SequenceReader<byte> reader = new SequenceReader<byte>(body);

            while (!reader.End) {
                // read token up to '&'
                ReadOnlySequence<byte> pair;
                if (!reader.TryReadTo(out pair, (byte)'&', advancePastDelimiter: true)) {
                    // last chunk (no '&' found)
                    pair = reader.Sequence.Slice(reader.Position);
                    reader.Advance(pair.Length);
                }

                if (pair.IsEmpty) {
                    continue;
                }

                // split key/value by '='
                int eqIndex = IndexOfByte(pair, (byte)'=');

                ReadOnlySequence<byte> keySeq = eqIndex >= 0 ? pair.Slice(0, eqIndex) : pair;
                ReadOnlySequence<byte> valSeq = eqIndex >= 0 ? pair.Slice(pair.GetPosition(eqIndex + 1)) : ReadOnlySequence<byte>.Empty;

                if (keySeq.IsEmpty) {
                    continue;
                }

                string key = UrlDecodeUtf8(keySeq);

                // handle foo[] convention
                if (key.EndsWith("[]", StringComparison.Ordinal)) {
                    key = key.Substring(0, key.Length - 2);
                }

                string? value = valSeq.IsEmpty ? null : UrlDecodeUtf8(valSeq);

                if (result.TryGetValue(key, out var existing)) {
                    if (existing is List<string?> list) {
                        list.Add(value);
                    }
                    else {
                        result[key] = new List<string?> { existing as string, value };
                    }
                }
                else {
                    result[key] = value;
                }
            }

            return result;
        }

        /// <summary>
        /// Get MultipartFormData from an HttpRequest
        /// </summary>
        /// <param name="request"></param>
        /// <param name="maxParts"></param>
        /// <param name="maxFileBytes"></param>
        /// <returns></returns>
        /// <example>
        /// var form = request.BodyMultipart();
        /// if (form == null) {
        ///     response.Status(400).Text("Bad multipart").SendAsync();
        ///     return;
        /// }
        /// foreach (var f in form.Files) {
        ///     var safeName = Path.GetFileName(f.FileName); // must sanitize path !!
        ///     await File.WriteAllBytesAsync(Path.Combine("/tmp/uploads", safeName), f.Content.ToArray());
        /// }
        /// </example>
        public static MultipartFormData? BodyMultipart(this HttpRequest request, int maxParts = 200, int maxFileBytes = 50 * 1024 * 1024) { 
            return BodyMultipartParser.BodyMultipart(request, maxParts, maxFileBytes);
        }

        /// <summary>
        /// Get MultipartFormDataStream from an HttpRequest
        /// </summary>
        /// <param name="request"></param>
        /// <param name="onField"></param>
        /// <param name="onFile"></param>
        /// <param name="maxParts"></param>
        /// <param name="maxFileBytes"></param>
        /// <returns></returns>
        /// <example>
        /// /// request.BodyMultipartStream(
        /// onField: (k, v) => {
        ///     Console.WriteLine($"FIELD {k}={v}");
        /// },
        /// onFile: (info, content) => {
        ///     var safeName = Path.GetFileName(info.FileName); // must sanitize path !!
        ///     Directory.CreateDirectory("/tmp/uploads");
        /// 
        ///     var path = Path.Combine("/tmp/uploads", safeName);
        ///     using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        /// 
        ///     content.CopyTo(fs); // zero ToArray()
        ///     Console.WriteLine($"FILE {info.FieldName} => {path} ({info.Length} bytes, {info.ContentType})");
        /// });
        /// </example>
        public static bool BodyMultipartStream(this HttpRequest request, Action<string, string>? onField = null, Action<MultipartFileInfo, ReadOnlySequence<byte>>? onFile = null, int maxParts = 200, long maxFileBytes = 50 * 1024 * 1024) {
            return BodyMultipartParser.BodyMultipartStream(request, onField, onFile, maxParts, maxFileBytes);
        }

        /// <summary>
        /// Write ReadOnlySequence to a Stream
        /// </summary>
        public static void CopyTo(this ReadOnlySequence<byte> seq, Stream destination) {
            foreach (var mem in seq) {
                destination.Write(mem.Span);
            }
        }

        /// <summary>
        /// Write Async ReadOnlySequence to a Stream
        /// </summary>
        public static async Task CopyToAsync(this ReadOnlySequence<byte> seq, Stream destination, CancellationToken ct = default) {
            foreach (var mem in seq) {
                await destination.WriteAsync(mem, ct).ConfigureAwait(false);
            }
        }

        #endregion body

        #region helpers

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="json">The json string.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="jsonEngine">the json library to handle serialization/deserialization (default: JsonEngine)</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool JsonMap<TModel>(string json, TModel model, IJsonEngine jsonEngine, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null) {

            if (string.IsNullOrWhiteSpace(json)) {
                return false;
            }

            // deserialize and populate
            jsonEngine.Populate(json, model, includeProperties, excludeProperties);

            return true;
        }

        /// <summary>
        /// Alias for Utf8 encoding
        /// </summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// IndexOfByte
        /// </summary>
        /// <param name="seq"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static int IndexOfByte(in ReadOnlySequence<byte> seq, byte value) {
            if (seq.IsSingleSegment) {
                return seq.FirstSpan.IndexOf(value);
            }

            int offset = 0;
            foreach (var mem in seq) {
                int idx = mem.Span.IndexOf(value);
                if (idx >= 0) {
                    return offset + idx;
                }
                offset += mem.Length;
            }
            return -1;
        }

        /// <summary>
        /// ContainsPctOrPlus
        /// </summary>
        /// <param name="seq"></param>
        /// <returns></returns>
        private static bool ContainsPctOrPlus(in ReadOnlySequence<byte> seq) {
            foreach (var mem in seq) {
                var span = mem.Span;
                // IndexOfAny is fast on Span<byte>
                if (span.IndexOfAny((byte)'%', (byte)'+') >= 0) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// URL-decode x-www-form-urlencoded bytes into a string using UTF-8.
        /// - '+' => space
        /// - %HH => byte
        /// </summary>
        private static string UrlDecodeUtf8(in ReadOnlySequence<byte> input) {
            if (input.IsEmpty) {
                return string.Empty;
            }

            // fast path: no '%' and no '+', return UTF-8 as-is (token-only allocation)
            if (!ContainsPctOrPlus(input)) {
                if (input.IsSingleSegment) {
                    return Utf8.GetString(input.FirstSpan);
                }

                // rare: multi-segment token without encoding markers
                int len = checked((int)input.Length);
                byte[] rentedRaw = ArrayPool<byte>.Shared.Rent(len);
                try {
                    input.CopyTo(rentedRaw);
                    return Utf8.GetString(rentedRaw, 0, len);
                }
                finally {
                    ArrayPool<byte>.Shared.Return(rentedRaw);
                }
            }

            // decoding path (allocate max token length, then shrink via written)
            int maxLen = checked((int)input.Length);
            byte[] rented = ArrayPool<byte>.Shared.Rent(maxLen);

            try {
                int written = 0;
                SequenceReader<byte> r = new(input);

                while (!r.End) {
                    r.TryRead(out byte b);

                    if (b == (byte)'+') {
                        rented[written++] = (byte)' ';
                        continue;
                    }

                    if (b == (byte)'%'
                        && r.TryPeek(out byte b1)
                        && r.TryPeek(1, out byte b2)
                    ) {

                        int hi = FromHex(b1);
                        int lo = FromHex(b2);

                        if (hi >= 0 && lo >= 0) {
                            r.Advance(2);
                            rented[written++] = (byte)((hi << 4) | lo);
                            continue;
                        }
                        // invalid -> keep literal '%'
                    }

                    rented[written++] = b;
                }

                return Utf8.GetString(rented, 0, written);
            }
            finally {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        /// <summary>
        /// Return int from hex char
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        private static int FromHex(byte c) {
            if (c >= (byte)'0' && c <= (byte)'9') {
                return c - (byte)'0';
            }
            if (c >= (byte)'a' && c <= (byte)'f') {
                return 10 + (c - (byte)'a');
            }
            if (c >= (byte)'A' && c <= (byte)'F') {
                return 10 + (c - (byte)'A');
            }
            return -1;
        }

        /// <summary>
        /// normalize prefix to "/xxx" (no trailing slash unless it's "/")
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns></returns>
        public static string NormalizePrefix(string prefix) {
            prefix = prefix.Trim();
            if (prefix.Length == 0) {
                return "/";
            }
            if (!prefix.StartsWith("/")) {
                prefix = "/" + prefix;
            }
            if (prefix.Length > 1 && prefix.EndsWith('/')) {
                prefix = prefix.TrimEnd('/');
            }
            return prefix;
        }

        #endregion helpers

    }

    #region multiparts

    /// <summary>
    /// Multipart FormData
    /// </summary>
    /// <param name="Fields"></param>
    /// <param name="Files"></param>
    public sealed record MultipartFormData(IReadOnlyDictionary<string, string> Fields, IReadOnlyList<MultipartFile> Files);

    /// <summary>
    /// Multipart File
    /// </summary>
    /// <param name="FieldName"></param>
    /// <param name="FileName"></param>
    /// <param name="ContentType"></param>
    /// <param name="Content"></param>
    public sealed record MultipartFile(string FieldName, string FileName, string? ContentType, ReadOnlyMemory<byte> Content);

    /// <summary>
    /// Multipart FileInfo
    /// </summary>
    /// <param name="FieldName"></param>
    /// <param name="FileName"></param>
    /// <param name="ContentType"></param>
    /// <param name="Length"></param>
    public sealed record MultipartFileInfo(string FieldName, string FileName, string? ContentType, long Length);

    #endregion multiparts

}
