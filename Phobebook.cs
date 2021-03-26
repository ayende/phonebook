using System;
using System.Collections.Generic;
using System.IO;
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

        public class Node
        {
            public long Self = -1, Left = -1, Right = -1;
            public int Height;
            public sbyte BranchFactor;

            public const int SizeOfNodeHeader = (sizeof(long) * 2) + sizeof(int) + sizeof(sbyte);

            public Entry Entry;
            public string Key;

            public static Node Read(BinaryReader br, long pos, bool readEntry)
            {
                if (pos == -1) return null;
                br.BaseStream.Position = pos;
                var node = new Node
                {
                    Self = pos,
                };

                node.Left = br.ReadInt64();
                node.Right = br.ReadInt64();
                node.Height = br.ReadInt32();
                node.BranchFactor = br.ReadSByte();
                if (readEntry)
                    node.Entry = Entry.Read(br);
                // we need to read the key always
                node.Key = node.Entry == null ? br.ReadString() : node.Entry.Name;
                return node;
            }

            public long Write(BinaryWriter bw)
            {
                bw.BaseStream.Position = Entry != null ? bw.BaseStream.Length : Self;
                Self = bw.BaseStream.Position;
                bw.Write(Left);
                bw.Write(Right);
                bw.Write(Height);
                bw.Write(BranchFactor);
                if (Entry != null)
                {
                    Entry.Write(bw);
                }
                return Self;
            }

            private static int ReadHeight(BinaryReader br, long entryPos)
            {
                if (entryPos == -1)
                    return 0;
                br.BaseStream.Position = entryPos + sizeof(long) + sizeof(long);
                return br.ReadInt32();
            }


            public void UpdateHeight(BinaryReader br)
            {
                var leftHeight = Node.ReadHeight(br, Left);
                var rightHeight = Node.ReadHeight(br, Right);
                Height = Math.Max(leftHeight, rightHeight) + 1;
                BranchFactor = (sbyte)(leftHeight - rightHeight);
            }

            public Entry Find(BinaryReader br, string name)
            {
                var comp = string.Compare(name, Key);
                if(comp == 0)
                {
                    br.BaseStream.Position = Self + SizeOfNodeHeader;
                    return Entry.Read(br);
                }
                var child= Read(br, comp < 0 ? Left : Right, readEntry: false);
                return child?.Find(br, name);
            }

            public IEnumerable<Entry> IterateAfter(BinaryReader br,string afterName)
            {
                var left = Read(br, Left, readEntry: true);
                if (left != null && string.Compare(afterName, left.Key) <= 0)
                {
                    foreach (var item in left.IterateAfter(br, afterName))
                    {
                        yield return item;
                    }
                }
                if (string.Compare(afterName, Key) <= 0)
                    yield return Entry;
                var right = Read(br, Right, readEntry: true);
                if (right != null)
                {
                    foreach (var item in right.IterateAfter(br, afterName))
                    {
                        yield return item;
                    }
                }
            }
        }

        private readonly string _filename;

        public Phonebook(string filename)
        {
            _filename = filename;
        }


        public void InsertOrUpdate(Entry entry)
        {
            using var file = File.Open(_filename, FileMode.OpenOrCreate);
            using var bw = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
            using var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
            if (file.Length == 0)
            {
                long rootPos = WriteNewNode(entry, bw);
                bw.Write(rootPos); // end of file marker
                return;
            }
            file.Position = file.Length - sizeof(long);
            var root = Node.Read(br, br.ReadInt64(), readEntry: false);
            var newRootPos = InsertOrUpdate(br, bw, root, entry);
            file.Position = file.Length;
            bw.Write(newRootPos);
        }

        private static long WriteNewNode(Entry entry, BinaryWriter bw)
        {
            bw.BaseStream.Position = Math.Max(0, bw.BaseStream.Length - sizeof(long));
            var newRoot = new Node
            {
                Self = bw.BaseStream.Position,
                Height = 1,
                Left = -1,
                Right = -1,
                Entry = entry
            };
            newRoot.Write(bw);
            return newRoot.Self;
        }

        private long InsertOrUpdate(BinaryReader br, BinaryWriter bw, Node root, Entry entry)
        {
            var comp = string.Compare(entry.Name, root.Key);
            if (comp == 0)// update, write new node (may have different size)
            {
                bw.BaseStream.Position = bw.BaseStream.Length - sizeof(long);
                var newNode = new Node
                {
                    Self = bw.BaseStream.Position,
                    Left = root.Left,
                    Right = root.Right,
                    Height = root.Height,
                    Entry = entry
                };
                newNode.Write(bw);
                // no change to the node structure, no need to rebalance
                return newNode.Self;
            }

            if (comp < 0)
            {
                long left;
                if (root.Left == -1)
                {
                    left = WriteNewNode(entry, bw);
                }
                else
                {
                    left = InsertOrUpdate(br, bw,
                        Node.Read(br, root.Left, readEntry: false),
                        entry);
                }
                root.Left = left;
            }
            else
            {
                long right;
                if (root.Right == -1)
                {
                    right = WriteNewNode(entry, bw);
                }
                else
                {
                    right = InsertOrUpdate(br, bw,
                        Node.Read(br, root.Right, readEntry: false),
                        entry);
                }
                root.Right = right;
            }
            root.UpdateHeight(br);
            return Rebalance(br, bw, root);
        }


        private long Rebalance(BinaryReader br, BinaryWriter bw, Node root)
        {
            switch(root.BranchFactor)
            {
                case 2:
                    var left = Node.Read(br, root.Left, readEntry: false);
                    if(left.BranchFactor < 0)
                    {
                        root.Left = RotateLeft(br, bw, left);
                    }
                    return RotateRight(br, bw, root);
                case -2:
                    var right = Node.Read(br, root.Right, readEntry: false);
                    if (right.BranchFactor > 0)
                    {
                        root.Right= RotateRight(br, bw, right);
                    }
                    return RotateLeft(br, bw, root);
                default:
                    // no change
                    root.Write(bw);
                    return root.Self;
            }
        }

        private long RotateRight(BinaryReader br, BinaryWriter bw, Node root)
        {
            var pivot = Node.Read(br, root.Left, readEntry: false);
            var tmp = pivot.Right;
            pivot.Right = root.Self;
            root.Left = tmp;

            pivot.UpdateHeight(br);
            root.UpdateHeight(br);

            pivot.Write(bw);
            root.Write(bw);

            return pivot.Self;
        }

        private long RotateLeft(BinaryReader br, BinaryWriter bw, Node root)
        {
            var pivot = Node.Read(br, root.Right, readEntry: false);
            var tmp = pivot.Left;
            pivot.Left = root.Self;
            root.Right = tmp;

            pivot.UpdateHeight(br);
            root.UpdateHeight(br);

            pivot.Write(bw);
            root.Write(bw);

            return pivot.Self;
        }

        public Entry GetByName(string name)
        {
            using var file = File.Open(_filename, FileMode.OpenOrCreate);
            if (file.Length == 0) return null;
            using var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
            file.Position = file.Length - sizeof(long);
            var root = Node.Read(br, br.ReadInt64(), readEntry: false);
            return root.Find(br, name);
        }

        public IEnumerable<Entry> IterateOrderedByName(string afterName = null)
        {
            using var file = File.Open(_filename, FileMode.OpenOrCreate);
            if (file.Length == 0) yield break;
            using var br = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
            file.Position = file.Length - sizeof(long);
            var root = Node.Read(br, br.ReadInt64(), readEntry: true);
            foreach(var item in root.IterateAfter(br, afterName ?? string.Empty))
            {
                yield return item;
            }
        }
    }
}
