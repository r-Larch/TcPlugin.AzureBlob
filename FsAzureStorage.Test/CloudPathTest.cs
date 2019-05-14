using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;


namespace FsAzureStorage.Test {
    [TestClass]
    public class CloudPathTest {
        [TestMethod]
        public void Converts_Backslash()
        {
            CloudPath path = @"\Test\Folder\Path\File.txt";
            var actual = (string) path;
            var expected = "/Test/Folder/Path/File.txt";
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Removes_TrailingSlash()
        {
            CloudPath path = "/some/folder/";
            Assert.AreEqual("/some/folder", (string) path);

            CloudPath path2 = @"\some\folder\";
            Assert.AreEqual("/some/folder", (string) path2);
        }


        [TestMethod]
        public void BlobName()
        {
            CloudPath path = "/MyAccount/MyContainer/Folder1/Folder2/File.txt";
            var actual = path.BlobName;
            var expected = "Folder1/Folder2/File.txt";
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Prefix()
        {
            // does the same as BlobName
            CloudPath path = "/MyAccount/MyContainer/Folder1/Folder2/File.txt";
            var actual = path.Prefix;
            var expected = "Folder1/Folder2/File.txt";
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void IsBlobPath()
        {
            CloudPath trueBlobPath = "/MyAccount/MyContainer/Folder1/Folder2/File.txt";
            CloudPath falseBlobPath = "/MyAccount/MyContainer/";
            Assert.IsTrue(trueBlobPath.IsBlobPath);
            Assert.IsFalse(falseBlobPath.IsBlobPath);
        }

        [TestMethod]
        public void ContainerName()
        {
            CloudPath path = "/MyAccount/MyContainer/Folder1/Folder2/File.txt";
            var actual = path.ContainerName;
            var expected = "MyContainer";
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void AccountName()
        {
            CloudPath path = "/MyAccount/MyContainer/Folder1/Folder2/File.txt";
            var actual = path.AccountName;
            var expected = "MyAccount";
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Directory()
        {
            CloudPath path = "/MyAccount/MyContainer/Folder1/Folder2/File.txt";

            path = path.Directory;
            Assert.AreEqual("/MyAccount/MyContainer/Folder1/Folder2", (string) path);

            path = path.Directory;
            Assert.AreEqual("/MyAccount/MyContainer/Folder1", (string) path);

            path = path.Directory;
            Assert.AreEqual("/MyAccount/MyContainer", (string) path);

            path = path.Directory;
            Assert.AreEqual("/MyAccount", (string) path);

            path = path.Directory;
            Assert.AreEqual("/", (string) path);
        }


        [TestMethod]
        public void Level()
        {
            var level0 = (CloudPath) @"\";
            var level1 = (CloudPath) @"\segment";
            var level2 = (CloudPath) @"\segment\segment";

            foreach (var p in level0.Segments) {
                Console.WriteLine("0:" + p);
            }

            foreach (var p in level1.Segments) {
                Console.WriteLine("1:" + p);
            }

            foreach (var p in level2.Segments) {
                Console.WriteLine("2:" + p);
            }

            Assert.AreEqual(0, level0.Level);
            Assert.AreEqual(1, level1.Level);
            Assert.AreEqual(2, level2.Level);
        }


        [TestMethod]
        public void GetSegment()
        {
            CloudPath path = "/segment1/segment2/segment3/segment4";

            Assert.AreEqual(null, path.GetSegment(0));
            Assert.AreEqual("segment1", path.GetSegment(1));
            Assert.AreEqual("segment2", path.GetSegment(2));
            Assert.AreEqual("segment3", path.GetSegment(3));
            Assert.AreEqual("segment4", path.GetSegment(4));
        }
    }
}
