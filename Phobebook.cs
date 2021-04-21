using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace pb
{
    public class Phonebook
    {
        public class Entry
        {
            public const int EstimatedRecordLen = 64;
            public string Name;

            public string Number;

            public string Type;

            public override bool Equals(object obj)
            {
                return obj is Entry entry &&
                       Name == entry.Name &&
                       Number == entry.Number &&
                       Type == entry.Type;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Name, Number, Type);
            }

            public static Entry Read(BinaryReader br)
            {
                return new Entry
                {
                    Name = br.ReadString(),
                    Number = br.ReadString(),
                    Type = br.ReadString(),
                };
            }

            public void Write(BinaryWriter bw)
            {
                bw.Write(Name);
                bw.Write(Number ?? string.Empty);
                bw.Write(Type ?? string.Empty);
            }
        }

        private readonly string _directory;

        public Phonebook(string directory)
        {
            _directory = directory;
        }


        public void InsertOrUpdate(Entry entry)
        {
            long entryPos;
            using (var file = File.Open(Path.Combine(_directory, "records"), FileMode.OpenOrCreate))
            using (var bw = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
            {
                entryPos = file.Position = file.Length;
                entry.Write(bw);
            }

            using (var sorted = File.Open(Path.Combine(_directory, "sorted.last"), FileMode.OpenOrCreate))
            using (var bw = new BinaryWriter(sorted, Encoding.UTF8, leaveOpen: true))
            {
                sorted.Position = sorted.Length;
                bw.Write(entryPos);

                if (sorted.Length < 1024)
                    return;
            }

            Merge();
        }

        private interface IRandomAccess : IDisposable
        {
            long Current => this[CurrentIndex];

            int CurrentIndex { get; set; }
            int Count { get; }
            long this[int index] { get; }
        }

        private class ListRandomAccess : IRandomAccess
        {
            private readonly List<long> _items;

            public ListRandomAccess(List<long> items)
            {
                this._items = items;
            }
            public int CurrentIndex { get; set; }
            public long this[int index] => _items[index];

            public int Count => _items.Count;

            public void Dispose()
            {
            }
        }
        private class SlicedRandomAccess : IRandomAccess
        {
            private readonly IRandomAccess _inner;
            private readonly int _start;

            public SlicedRandomAccess(IRandomAccess inner, int start)
            {
                _inner = inner;
                _start = start;
            }

            public long this[int index] => _inner[index + _start];

            public int Count => _inner.Count - _start;

            public int CurrentIndex { get; set; }

            public void Dispose()
            {
                _inner.Dispose();
            }
        }

        private class FileRandomAccess : IRandomAccess
        {
            BinaryReader _br;

            public FileRandomAccess(BinaryReader br)
            {
                _br = br;
            }
            public int CurrentIndex { get; set; }
            public long this[int index]
            {
                get
                {
                    _br.BaseStream.Position = index * sizeof(long);
                    return _br.ReadInt64();
                }
            }

            public int Count => (int)(_br.BaseStream.Length / sizeof(long));

            public void Dispose()
            {
                _br.Dispose();
            }
        }

        private IRandomAccess ReadLastSorted()
        {
            using (var file = File.Open(Path.Combine(_directory, "records"), FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read))
            using (var recordsReader = new BinaryReader(file))
            using (var sorted = File.Open(Path.Combine(_directory, "sorted.last"), FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(sorted))
            {
                var dic = new SortedDictionary<string, long>();
                while (sorted.Position < sorted.Length)
                {
                    var pos = br.ReadInt64();
                    recordsReader.BaseStream.Position = pos;
                    dic[recordsReader.ReadString()] = pos;
                }
                return new ListRandomAccess(dic.Values.ToList());
            }
        }

        private IRandomAccess ReadSorted(string filename)
        {
            var sorted = File.Open(filename, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
            var br = new BinaryReader(sorted);
            return new FileRandomAccess(br);

        }

        private long MergeSortedPositions(List<IRandomAccess> toMerge)
        {
            using var file = File.Open(Path.Combine(_directory, "records"), FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

            long deletes = 0;
            using var sorted = File.Open(Path.Combine(_directory, $"sorted.{DateTime.UtcNow:yyyy-MM-ddThh-mm-ss}-{Guid.NewGuid()}"), FileMode.OpenOrCreate);
            using var bw = new BinaryWriter(sorted, Encoding.UTF8, leaveOpen: true);
            foreach (var pos in MergeSorted(toMerge, br))
            {
                if (pos < 0)
                {
                    deletes++;
                    continue;
                }
                bw.Write(pos);
            }
            return deletes;
        }

        private static IEnumerable<long> MergeSorted(List<IRandomAccess> toMerge, BinaryReader br)
        {
            var heap = new SortedList<string, IRandomAccess>();
            foreach (var it in toMerge)
            {
                it.CurrentIndex = -1;
                TryAddToHeap(it);
            }

            while (heap.Count > 0)
            {
                var it = heap.Values[0];
                heap.RemoveAt(0);
                yield return it.Current;
                TryAddToHeap(it);
            }

            void TryAddToHeap(IRandomAccess it)
            {
                it.CurrentIndex++;
                if (it.CurrentIndex >= it.Count)
                {
                    it.Dispose();
                    return;
                }
                var cur = it.Current;
                if (cur < 0)
                    cur = ~cur;
                br.BaseStream.Position = cur;
                heap.Add(br.ReadString(), it);
            }
        }

        private void Compact()
        {
            using var file = File.Open(Path.Combine(_directory, "records"), FileMode.OpenOrCreate);
            using var newFile = File.Open(Path.Combine(_directory, "records.new"), FileMode.OpenOrCreate);
            using var bw = new BinaryWriter(newFile, Encoding.UTF8, leaveOpen: true);
            using var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

            var positionsToMerge = GetSortedPositions();
            foreach (var pos in MergeSorted(positionsToMerge, br))
            {
                if (pos < 0)
                {
                    continue;
                }
                br.BaseStream.Position = pos;
                var entry = Entry.Read(br);
                entry.Write(bw);
            }
        }

        private List<IRandomAccess> GetSortedPositions()
        {
            var toMerge = new List<IRandomAccess>
            {
                ReadLastSorted()
            };

            toMerge.AddRange(
                Directory.GetFiles(_directory, "sorted.*")
                .Where(f => Path.GetExtension(f) != ".last")
                .Select(f => ReadSorted(f))
            );
            return toMerge;
        }

        public void Merge()
        {
            var toMerge = new List<IRandomAccess>
            {
                ReadLastSorted()
            };

            var files = Directory.GetFiles(_directory, "sorted.*")
                .Where(f => Path.GetExtension(f) != ".last")
                .GroupBy(f => (int)Math.Log2(new FileInfo(f).Length)) // same size in power by 2, basically
                .OrderBy(g => g.Key)
                .FirstOrDefault()?.ToList() ?? Enumerable.Empty<string>();

            foreach (var file in files)
            {
                toMerge.Add(ReadSorted(file));
            }

            long deletes = MergeSortedPositions(toMerge);

            foreach (var file in files)
            {
                File.Delete(file);
            }
            File.Delete(Path.Combine(_directory, "sorted.last"));
            using var statsFile = File.Open(Path.Combine(_directory, "metadata.stats"), FileMode.OpenOrCreate);
            using var bw = new BinaryWriter(statsFile, Encoding.UTF8, leaveOpen: true);
            using var br = new BinaryReader(statsFile, Encoding.UTF8, leaveOpen: true);

            if (statsFile.Length > 0)
            {
                deletes += br.ReadInt64();
            }


            var recordsLen = new FileInfo(Path.Combine(_directory, "records")).Length / Entry.EstimatedRecordLen;

            if (deletes * 4 > recordsLen)
            {
                Compact();
                deletes = 0;
            }

            statsFile.Position = 0;
            bw.Write(deletes);
        }
        private int Search(IRandomAccess access, BinaryReader br, string name)
        {
            int low = 0;
            int high = access.Count - 1;
            int mid = 0;
            while (low <= high)
            {
                mid = low + (high - low) / 2;
                var pos = access[mid];
                if (pos < 0)
                    pos = ~pos;
                br.BaseStream.Position = pos;
                var curName = br.ReadString();
                var comp = string.Compare(name, curName);
                if (comp == 0)
                    return mid;
                if (comp < 0)
                    high = mid - 1;
                else
                    low = mid + 1;
            }
            return ~low;
        }
        public Entry GetByName(string name)
        {
            using var file = File.Open(Path.Combine(_directory, "records"), FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
            foreach (var entriesPositions in GetSortedPositions())
            {
                var pos = Search(entriesPositions, br, name);
                if (pos < 0)
                    continue;
                var filePos = entriesPositions[pos];
                if (filePos < 0) return null; // deleted
                br.BaseStream.Position = filePos;
                return Entry.Read(br);
            }
            return null;
        }


        public IEnumerable<Entry> IterateOrderedByName(string afterName = null)
        {
            var positions = GetSortedPositions();

            using var file = File.Open(Path.Combine(_directory, "records"), FileMode.OpenOrCreate);
            using var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

            for (int i = 0; i < positions.Count; i++)
            {
                var pos = Search(positions[i], br, afterName ?? string.Empty);
                if (pos < 0)
                    pos = ~pos;
                positions[i] = new SlicedRandomAccess(positions[i], pos);
            }
            foreach (var pos in MergeSorted(positions, br))
            {
                br.BaseStream.Position = pos;
                yield return Entry.Read(br);
            }
        }
    }
}
