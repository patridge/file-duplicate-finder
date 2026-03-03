using System;
using System.IO;
using FileDeduplicator.Common;
using NUnit.Framework;

namespace FileDeduplicator.Tests
{
    [TestFixture]
    public class BinaryFileComparerTests
    {

        private string _file1;
        private string _file2;

        [SetUp]
        public void SetUp()
        {
            _file1 = Path.GetTempFileName();
            _file2 = Path.GetTempFileName();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_file1)) File.Delete(_file1);
            if (File.Exists(_file2)) File.Delete(_file2);
        }

        [Test]
        public void IdenticalFiles_AreEquivalent()
        {
            File.WriteAllText(_file1, "test content");
            File.Copy(_file1, _file2, true);
            var comparer = new BinaryFileComparer();

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.True);
        }

        [Test]
        public void DifferentContent_AreNotEquivalent()
        {
            File.WriteAllText(_file1, "abc");
            File.WriteAllText(_file2, "def");
            var comparer = new BinaryFileComparer();

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.False);
        }

        [Test]
        public void DifferentTimestamps_IgnoredIfConfigured()
        {
            File.WriteAllText(_file1, "same");
            File.Copy(_file1, _file2, true);
            File.SetLastWriteTimeUtc(_file2, DateTime.UtcNow.AddHours(-1));
            var comparer = new BinaryFileComparer { IgnoreMetadata = true };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.True);
        }

        [Test]
        public void SameContent_DifferentTimestamps_NotEquivalentByDefault()
        {
            File.WriteAllText(_file1, "same");
            File.Copy(_file1, _file2, true);
            File.SetLastWriteTimeUtc(_file2, DateTime.UtcNow.AddHours(-1));
            var comparer = new BinaryFileComparer();

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.False);
        }

        [Test]
        public void DifferentContent_NotEquivalent_EvenWhenIgnoringTimestamps()
        {
            File.WriteAllText(_file1, "abc");
            File.WriteAllText(_file2, "def");
            var comparer = new BinaryFileComparer { IgnoreMetadata = true };

            var result = comparer.AreFilesEquivalent(_file1, _file2);

            Assert.That(result, Is.False);
        }
    }
}
