using System.IO;
using System;
using Xunit;
using System.Linq;

namespace pb
{
    public class PhonebookTests : IDisposable
    {
        private string _fileName = Guid.NewGuid().ToString();

        public void Dispose()
        {
            File.Delete(_fileName);
        }

        [Fact]
        public void EmptyPhoneBookHasNoEntries()
        {
            var pb = new Phonebook(_fileName);
            Assert.Empty(pb.IterateOrderedByName());
        }

        [Fact]
        public void CanInsertAndGetResults()
        {
            var pb = new Phonebook(_fileName);
            var inserted = new Phonebook.Entry{
                Name = "Oren",
                Number = "1234",
                Type = "test"
            };

            pb.InsertOrUpdate(inserted);
            Assert.Single(pb.IterateOrderedByName());
            var itEntry = pb.IterateOrderedByName().First();
            var getEntry = pb.GetByName(inserted.Name);

            Assert.Equal(inserted, itEntry);
            Assert.Equal(inserted, getEntry);
        }

        [Fact]
        public void CanUpdateData()
        {
            var pb = new Phonebook(_fileName);
            var inserted = new Phonebook.Entry{
                Name = "Oren",
                Number = "1234",
                Type = "test"
            };

            pb.InsertOrUpdate(inserted);
            inserted.Number = "54321";
            pb.InsertOrUpdate(inserted);

            Assert.Single(pb.IterateOrderedByName());
            var itEntry = pb.IterateOrderedByName().First();
            var getEntry = pb.GetByName(inserted.Name);

            Assert.Equal(inserted, itEntry);
            Assert.Equal(inserted, getEntry);
        }

        [Fact]
        public void CanInsertMultipleResults()
        {
            var pb = new Phonebook(_fileName);
            var inputs = new[]
            {
                new Phonebook.Entry{Name = "Oren",Number = "1234",Type = "test"},
                new Phonebook.Entry{Name = "Ayende",Number = "73743",Type = "work"},
                new Phonebook.Entry{Name = "Marta",Number = "39391",Type = "home"},
                new Phonebook.Entry{Name = "Abc",Number = "1231",Type = "office"},
            };
            foreach (var item in inputs)
            {
                pb.InsertOrUpdate(item);
            }
            
            Array.Sort(inputs, (x, y) => x.Name.CompareTo(y.Name));

            Assert.Equal(inputs, pb.IterateOrderedByName().ToArray());
            // partial (sorted) read
            Assert.Equal(inputs.Skip(1).ToList(), pb.IterateOrderedByName("Ayende").ToArray());

            foreach (var item in inputs)
            {
                Assert.Equal(item, pb.GetByName(item.Name));
            }
        }
    }
}