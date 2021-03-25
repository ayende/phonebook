using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace pb
{
    public class Phonebook
    {
        public class Entry
        {
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
        }

        private readonly string _filename;

        public Phonebook(string filename)
        {
            _filename = filename;
        }

        private class FileFormat : IDisposable
        {
            private readonly Stream _stream;
            private readonly BinaryReader _br;
            private readonly BinaryWriter _bw;
            private readonly List<int> _sortedPositions = new List<int>();
            private int _endOfData;

            public int Count => _sortedPositions.Count;

            public FileFormat(Stream stream)
            {
                _stream = stream;
                _br = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
                _bw = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);

                if (_stream.Length == 0)
                    return;

                _stream.Position = _stream.Length - sizeof(int);
                _endOfData = _br.ReadInt32();
                _stream.Position = _endOfData; 
                using var gzip = new GZipStream(_stream, CompressionMode.Decompress, leaveOpen: true);
                using var gzipBR = new BinaryReader(gzip);
                var numOfRecords = gzipBR.ReadInt32();
                for (int i = 0; i < numOfRecords; i++)
                {
                    _sortedPositions.Add(gzipBR .ReadInt32());
                }
            }

            public void Dispose()
            {
                _br?.Dispose();
                _bw?.Dispose();
            }

            public int Search(string name)
            {
                int low = 0;
                int high = _sortedPositions.Count - 1;
                int mid = 0;
                while (low <= high)
                {
                    mid = (low + high) >> 1;
                    _stream.Position = _sortedPositions[mid];
                    var curName = _br.ReadString();
                    var comp = string.Compare(name, curName);
                    if (comp == 0)
                        return mid;
                    if (comp < 0)
                        high = mid - 1;
                    else
                        low = mid + 1;
                }
                return ~mid;
            }

            public void Insert(Entry e, int pos)
            {
                if (pos < 0)
                {
                    _sortedPositions.Insert(~pos, _endOfData);
                }
                else
                {
                    _sortedPositions[pos] = _endOfData;
                }

                _stream.Position = _endOfData;
                _bw.Write(e.Name);
                _bw.Write(e.Number);
                _bw.Write(e.Type);

                _endOfData = (int)_stream.Position;
            }

            public void Flush()
            {
                _stream.Position = _endOfData;
                using var gzip = new GZipStream(_stream, CompressionLevel.Optimal, leaveOpen: true);
                {
                    using var gzipBW = new BinaryWriter(gzip);
                    {
                        gzipBW.Write(_sortedPositions.Count);
                        foreach (var p in _sortedPositions)
                        {
                            gzipBW.Write(p);
                        }
                    }
                }
                _bw.Write(_endOfData);
            }

            public Entry GetEntry(int pos)
            {
                _stream.Position = _sortedPositions[pos];
                return new Entry
                {
                    Name = _br.ReadString(),
                    Number = _br.ReadString(),
                    Type = _br.ReadString()
                };

            }
        }

        public void InsertOrUpdate(Entry entry)
        {
            using var file = File.Open(_filename, FileMode.OpenOrCreate);
            using var ff = new FileFormat(file);
            var pos = ff.Search(entry.Name);
            ff.Insert(entry, pos);
            ff.Flush();
        }

        public Entry GetByName(string name)
        {
            using var file = File.Open(_filename, FileMode.OpenOrCreate);
            using var ff = new FileFormat(file);
            var pos = ff.Search(name);
            if (pos < 0)
                return null;
            return ff.GetEntry(pos);
        }

        public IEnumerable<Entry> IterateOrderedByName(string afterName = null)
        {
            using var file = File.Open(_filename, FileMode.OpenOrCreate);
            using var ff = new FileFormat(file);
            var pos = ff.Search(afterName ?? string.Empty);
            if (pos < 0)
                pos = ~pos;
            for (int i = pos; i < ff.Count; i++)
            {
                yield return ff.GetEntry(i);
            }
        }
    }
}
