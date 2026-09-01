using System.Threading;

namespace SimpleW.Service.FileBrowser {

    internal sealed class UploadFileState {

        private readonly List<ReceivedRange> _ranges = new();

        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string RelativePath { get; }
        public string TargetFullPath { get; }
        public string TempPath { get; }
        public long Size { get; }
        public long ReceivedBytes { get; private set; }
        public bool Completed { get; set; }
        public bool IsComplete => Size == 0 || (_ranges.Count == 1 && _ranges[0].Start == 0 && _ranges[0].End == Size);

        public UploadFileState(string relativePath, string targetFullPath, string tempPath, long size) {
            RelativePath = relativePath;
            TargetFullPath = targetFullPath;
            TempPath = tempPath;
            Size = size;
        }

        public void SetSingleRange(long length) {
            _ranges.Clear();
            _ranges.Add(new ReceivedRange(0, length));
            ReceivedBytes = length;
        }

        public void AddRange(long start, long length) {
            long end = start + length;
            _ranges.Add(new ReceivedRange(start, end));
            _ranges.Sort(static (a, b) => a.Start.CompareTo(b.Start));

            List<ReceivedRange> merged = new(_ranges.Count);
            foreach (ReceivedRange range in _ranges) {
                if (merged.Count == 0 || range.Start > merged[^1].End) {
                    merged.Add(range);
                }
                else if (range.End > merged[^1].End) {
                    ReceivedRange current = merged[^1];
                    merged[^1] = new ReceivedRange(current.Start, range.End);
                }
            }

            _ranges.Clear();
            _ranges.AddRange(merged);
            ReceivedBytes = _ranges.Sum(static r => r.End - r.Start);
        }

    }

}
