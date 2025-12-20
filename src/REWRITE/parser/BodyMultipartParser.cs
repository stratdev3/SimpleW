using System.Buffers;
using System.Text;


namespace SimpleW {

    /// <summary>
    /// Body MultipartParser
    /// </summary>
    public static class BodyMultipartParser {

        #region multipart

        /// <summary>
        /// Get MultipartFormData from an HttpRequest
        /// </summary>
        /// <param name="request"></param>
        /// <param name="maxParts"></param>
        /// <param name="maxFileBytes"></param>
        /// <returns></returns>
        public static MultipartFormData? BodyMultipart(HttpRequest request, int maxParts = 200, int maxFileBytes = 50 * 1024 * 1024) {

            if (request.Body.IsEmpty) {
                return null;
            }

            string? contentType = request.Headers.ContentType;
            if (string.IsNullOrWhiteSpace(contentType)) {
                return null;
            }

            if (!TryGetBoundary(contentType, out var boundary)) {
                return null;
            }

            // body : a single-segment most of the time (Content-Length or chunked)
            ReadOnlyMemory<byte> bodyMem;
            if (request.Body.IsSingleSegment) {
                bodyMem = request.Body.First; // zero-copy
            }
            else {
                // fallback safe
                int len = checked((int)request.Body.Length);
                byte[] tmp = new byte[len];
                request.Body.CopyTo(tmp);
                bodyMem = tmp;
            }

            return ParseBodyMultipart(bodyMem, boundary, maxParts, maxFileBytes);
        }

        /// <summary>
        /// Parse BodyMultipart
        /// </summary>
        /// <param name="body"></param>
        /// <param name="boundary"></param>
        /// <param name="maxParts"></param>
        /// <param name="maxFileBytes"></param>
        /// <returns></returns>
        private static MultipartFormData? ParseBodyMultipart(ReadOnlyMemory<byte> body, string boundary, int maxParts, int maxFileBytes) {

            Dictionary<string, string> fields = new(StringComparer.Ordinal);
            List<MultipartFile> files = new();

            byte[]? boundaryLine = Encoding.ASCII.GetBytes("--" + boundary);
            byte[]? boundaryEnd = Encoding.ASCII.GetBytes("--" + boundary + "--");

            ReadOnlySpan<byte> span = body.Span;
            int pos = IndexOf(span, boundaryLine, 0);
            if (pos < 0) {
                return null;
            }

            while (true) {
                if (maxParts-- <= 0) {
                    return null;
                }

                // end ?
                if (StartsWithAt(span, boundaryEnd, pos)) {
                    break;
                }

                // consume "--boundary" + CRLF
                pos += boundaryLine.Length;
                if (!ConsumeCrlf(span, ref pos)) {
                    return null;
                }

                // headers
                if (!TryReadHeaders(span, ref pos, out var headers)) {
                    return null;
                }

                // content until next "\r\n--boundary"
                int nextBoundary = FindNextBoundary(span, boundaryLine, pos);
                if (nextBoundary < 0) {
                    return null;
                }

                int contentEnd = nextBoundary;
                if (contentEnd >= 2 && span[contentEnd - 2] == (byte)'\r' && span[contentEnd - 1] == (byte)'\n') {
                    contentEnd -= 2;
                }

                ReadOnlyMemory<byte> content = body.Slice(pos, contentEnd - pos);
                pos = nextBoundary;

                // content-disposition obligatoire
                if (!headers.TryGetValue("content-disposition", out var cd)) {
                    return null;
                }

                if (!TryParseContentDisposition(cd, out var name, out var filename)) {
                    return null;
                }

                headers.TryGetValue("content-type", out var partCt);

                if (filename is null) {
                    fields[name] = Encoding.UTF8.GetString(content.Span);
                }
                else {
                    if (content.Length > maxFileBytes) {
                        return null;
                    }

                    files.Add(
                        new MultipartFile(
                            FieldName: name,
                            FileName: filename,
                            ContentType: partCt,
                            Content: content
                        )
                    );
                }

                // stop if next boundary is end
                if (StartsWithAt(span, boundaryEnd, pos))
                    break;
            }

            return new MultipartFormData(fields, files);
        }

        #endregion multipart

        #region multipart stream

        /// <summary>
        /// Get MultipartFormDataStream from an HttpRequest
        /// Parse multipart/form-data and stream each part to callbacks (no file ToArray()).
        /// Returns false if not multipart or invalid
        /// </summary>
        /// <param name="request"></param>
        /// <param name="onField"></param>
        /// <param name="onFile"></param>
        /// <param name="maxParts"></param>
        /// <param name="maxFileBytes"></param>
        /// <returns></returns>
        public static bool BodyMultipartStream(HttpRequest request, Action<string, string>? onField = null, Action<MultipartFileInfo, ReadOnlySequence<byte>>? onFile = null, int maxParts = 200, long maxFileBytes = 50 * 1024 * 1024) {

            if (request.Body.IsEmpty) {
                return false;
            }

            string? contentType = request.Headers.ContentType;
            if (string.IsNullOrWhiteSpace(contentType)) {
                return false;
            }

            if (!TryGetBoundary(contentType, out var boundary)) {
                return false;
            }

            // body : a single-segment most of the time (Content-Length or chunked)
            // so we set a fast path span-based + convert offset->ReadOnlySequence slice
            return ParseBodyMultipartStream(request.Body, boundary, onField, onFile, maxParts, maxFileBytes);
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

        /// <summary>
        /// Parse BodyMultipartStream
        /// </summary>
        /// <param name="body"></param>
        /// <param name="boundary"></param>
        /// <param name="onField"></param>
        /// <param name="onFile"></param>
        /// <param name="maxParts"></param>
        /// <param name="maxFileBytes"></param>
        /// <returns></returns>
        private static bool ParseBodyMultipartStream(ReadOnlySequence<byte> body, string boundary, Action<string, string>? onField, Action<MultipartFileInfo, ReadOnlySequence<byte>>? onFile, int maxParts, long maxFileBytes) {
            // On bosse sur un span pour scanner vite.
            // Si multi segment, on copie dans un buffer stable (rare, mais safe).
            ReadOnlySpan<byte> span;
            byte[]? rented = null;
            int length = checked((int)body.Length);

            if (body.IsSingleSegment) {
                span = body.FirstSpan;
            }
            else {
                rented = ArrayPool<byte>.Shared.Rent(length);
                body.CopyTo(rented);
                span = rented.AsSpan(0, length);
                body = new ReadOnlySequence<byte>(rented, 0, length);
            }

            try {
                var boundaryLine = Encoding.ASCII.GetBytes("--" + boundary);
                var boundaryEnd = Encoding.ASCII.GetBytes("--" + boundary + "--");

                int pos = IndexOf(span, boundaryLine, 0);
                if (pos < 0)
                    return false;

                while (true) {
                    if (maxParts-- <= 0)
                        return false;

                    if (StartsWithAt(span, boundaryEnd, pos))
                        break;

                    pos += boundaryLine.Length;
                    if (!ConsumeCrlf(span, ref pos))
                        return false;

                    if (!TryReadHeaders(span, ref pos, out var headers))
                        return false;

                    int nextBoundary = FindNextBoundary(span, boundaryLine, pos);
                    if (nextBoundary < 0)
                        return false;

                    int contentEnd = nextBoundary;
                    if (contentEnd >= 2 && span[contentEnd - 2] == (byte)'\r' && span[contentEnd - 1] == (byte)'\n')
                        contentEnd -= 2;

                    int contentLen = contentEnd - pos;
                    if (contentLen < 0)
                        return false;

                    if (!headers.TryGetValue("content-disposition", out var cd))
                        return false;

                    if (!TryParseContentDisposition(cd, out var name, out var filename))
                        return false;

                    headers.TryGetValue("content-type", out var partCt);

                    // Slice “streamable”
                    var contentSeq = body.Slice(pos, contentLen);

                    if (filename is null) {
                        // Field: on decode en UTF-8 (ça alloue une string, normal)
                        string value;
                        if (contentSeq.IsSingleSegment) {
                            value = Encoding.UTF8.GetString(contentSeq.FirstSpan);
                        }
                        else {
                            // rare ici, mais au cas où
                            byte[] tmp = ArrayPool<byte>.Shared.Rent(contentLen);
                            try {
                                contentSeq.CopyTo(tmp);
                                value = Encoding.UTF8.GetString(tmp, 0, contentLen);
                            }
                            finally {
                                ArrayPool<byte>.Shared.Return(tmp);
                            }
                        }
                        onField?.Invoke(name, value);
                    }
                    else {
                        if (contentLen > maxFileBytes)
                            return false;

                        onFile?.Invoke(
                            new MultipartFileInfo(
                                FieldName: name,
                                FileName: filename,
                                ContentType: partCt,
                                Length: contentLen),
                            contentSeq);
                    }

                    pos = nextBoundary;

                    if (StartsWithAt(span, boundaryEnd, pos))
                        break;
                }

                return true;
            }
            finally {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        #endregion multipart stream

        #region helpers

        private static bool TryGetBoundary(string contentType, out string boundary) {
            boundary = "";

            // multipart/form-data; boundary=....
            string[]? parts = contentType.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) {
                return false;
            }

            if (!parts[0].Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            foreach (var p in parts) {
                if (p.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase)) {
                    boundary = p.Substring("boundary=".Length).Trim();
                    if (boundary.Length >= 2 && boundary[0] == '"' && boundary[^1] == '"') {
                        boundary = boundary[1..^1];
                    }

                    return boundary.Length > 0;
                }
            }

            return false;
        }

        private static bool TryReadHeaders(ReadOnlySpan<byte> span, ref int pos, out Dictionary<string, string> headers) {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (true) {
                // CRLF => headers end
                if (pos + 1 < span.Length && span[pos] == (byte)'\r' && span[pos + 1] == (byte)'\n') {
                    pos += 2;
                    return true;
                }

                int lineEnd = IndexOfCrlf(span, pos);
                if (lineEnd < 0) {
                    return false;
                }

                ReadOnlySpan<byte> line = span.Slice(pos, lineEnd - pos);
                pos = lineEnd + 2;

                int colon = line.IndexOf((byte)':');
                if (colon <= 0) {
                    return false;
                }

                string name = Encoding.ASCII.GetString(line.Slice(0, colon)).Trim();
                string value = Encoding.ASCII.GetString(line.Slice(colon + 1)).Trim();
                headers[name] = value;
            }
        }

        private static bool TryParseContentDisposition(string value, out string name, out string? filename) {
            name = "";
            filename = null;

            // form-data; name="x"; filename="y"
            var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) {
                return false;
            }
            if (!parts[0].Equals("form-data", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            foreach (var p in parts) {
                if (p.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) {
                    name = Unquote(p.Substring("name=".Length).Trim());
                }
                else if (p.StartsWith("filename=", StringComparison.OrdinalIgnoreCase)) {
                    filename = Unquote(p.Substring("filename=".Length).Trim());
                }
            }

            return name.Length > 0;
        }

        private static string Unquote(string s) => (s.Length >= 2 && s[0] == '"' && s[^1] == '"') ? s[1..^1] : s;

        private static int FindNextBoundary(ReadOnlySpan<byte> span, byte[] boundaryLine, int start) {
            // find "\r\n--boundary"
            for (int i = start; i <= span.Length - (boundaryLine.Length + 2); i++) {
                if (span[i] == (byte)'\r' && span[i + 1] == (byte)'\n' &&
                    span.Slice(i + 2, boundaryLine.Length).SequenceEqual(boundaryLine)) {
                    return i + 2;
                }
            }
            return -1;
        }

        private static bool ConsumeCrlf(ReadOnlySpan<byte> span, ref int pos) {
            if (pos + 1 >= span.Length) {
                return false;
            }
            if (span[pos] != (byte)'\r' || span[pos + 1] != (byte)'\n') {
                return false;
            }
            pos += 2;
            return true;
        }

        private static int IndexOfCrlf(ReadOnlySpan<byte> span, int start) {
            for (int i = start; i + 1 < span.Length; i++) {
                if (span[i] == (byte)'\r' && span[i + 1] == (byte)'\n') {
                    return i;
                }
            }
            return -1;
        }

        private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int start) {
            for (int i = start; i <= haystack.Length - needle.Length; i++) {
                if (haystack[i] == needle[0] && haystack.Slice(i, needle.Length).SequenceEqual(needle)) {
                    return i;
                }
            }
            return -1;
        }

        private static bool StartsWithAt(ReadOnlySpan<byte> span, ReadOnlySpan<byte> needle, int pos) {
            return pos >= 0
                   && pos + needle.Length <= span.Length
                   && span.Slice(pos, needle.Length).SequenceEqual(needle);
        }

        #endregion helpers

    }

}

