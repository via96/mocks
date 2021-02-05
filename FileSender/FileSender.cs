using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using FakeItEasy;
using FileSender.Dependencies;
using FluentAssertions;
using NUnit.Framework;
using System.ComponentModel;

namespace FileSender
{
    public class FileSender
    {
        private readonly ICryptographer cryptographer;
        private readonly ISender sender;
        private readonly IRecognizer recognizer;

        public FileSender(
            ICryptographer cryptographer,
            ISender sender,
            IRecognizer recognizer)
        {
            this.cryptographer = cryptographer;
            this.sender = sender;
            this.recognizer = recognizer;
        }

        public Result SendFiles(File[] files, X509Certificate certificate)
        {
            return new Result
            {
                SkippedFiles = files
                    .Where(file => !TrySendFile(file, certificate))
                    .ToArray()
            };
        }

        private bool TrySendFile(File file, X509Certificate certificate)
        {
            Document document;
            if (!recognizer.TryRecognize(file, out document))
                return false;
            if (!CheckFormat(document) || !CheckActual(document))
                return false;
            var signedContent = cryptographer.Sign(document.Content, certificate);
            return sender.TrySend(signedContent);
        }

        private bool CheckFormat(Document document)
        {
            return document.Format == "4.0" ||
                   document.Format == "3.1";
        }

        private bool CheckActual(Document document)
        {
            return document.Created.AddMonths(1) > DateTime.Now;
        }

        public class Result
        {
            public File[] SkippedFiles { get; set; }
        }
    }
    
    public enum DocumentFormat
    {
        [System.ComponentModel.Description("3.1")]
        Version31,
        [System.ComponentModel.Description("4.0")]
        Version40,
        [System.ComponentModel.Description("2.2")]
        Invalid
    }

    //TODO: реализовать недостающие тесты
    [TestFixture]
    public class FileSender_Should
    {
        private FileSender fileSender;
        private ICryptographer cryptographer;
        private ISender sender;
        private IRecognizer recognizer;

        private readonly X509Certificate certificate = new X509Certificate();
        private File file;
        private byte[] signedContent;

        private Random random;

        [SetUp]
        public void SetUp()
        {
            random = new Random();

            file = new File("someFile", new byte[] {1, 2, 3});
            signedContent = new byte[] {1, 7};

            cryptographer = A.Fake<ICryptographer>();
            sender = A.Fake<ISender>();
            recognizer = A.Fake<IRecognizer>();
            fileSender = new FileSender(cryptographer, sender, recognizer);
            
            A.CallTo(() => cryptographer.Sign(A<byte[]>.Ignored, certificate))
                .Returns(GenerateFileContent());
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored))
                .Returns(true);
        }

        [TestCase("4.0")]
        [TestCase("3.1")]
        public void Send_WhenGoodFormat(string format)
        {
            var document = new Document(file.Name, file.Content, DateTime.Now, format);
            file = RecognizeDocument(document);
            A.CallTo(() => sender.TrySend(signedContent))
                .Returns(true);

            fileSender.SendFiles(new[] {file}, certificate)
                .SkippedFiles.Should().BeEmpty();
        }

        [Test]
        public void Skip_WhenBadFormat()
        {
            var document = GenerateDocument(DocumentFormat.Invalid, DateTime.Now);
            file = RecognizeDocument(document);
            
            fileSender.SendFiles(new[] {file}, certificate)
                .SkippedFiles.Should().Contain(new File[] {file});
            
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored)).MustNotHaveHappened();
        }

        [Test]
        public void Skip_WhenOlderThanAMonth()
        {
            var document = GenerateDocument(DocumentFormat.Version40, DateTime.Now.AddMonths(-1));
            file = RecognizeDocument(document);
            
            fileSender.SendFiles(new[] {file}, certificate)
                .SkippedFiles.Should().Contain(new File[] {file});
            
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored)).MustNotHaveHappened();
        }

        [Test]
        public void Send_WhenYoungerThanAMonth()
        {
            var document = GenerateDocument(DocumentFormat.Version40, DateTime.Now.AddMonths(1));
            file = RecognizeDocument(document);
            
            fileSender.SendFiles(new[] {file}, certificate)
                .SkippedFiles.Should().BeEmpty();
            
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored)).MustHaveHappened(1, Times.Exactly);
        }

        [Test]
        public void Skip_WhenSendFails()
        {
            var document = GenerateDocument(DocumentFormat.Version40, DateTime.Now);
            file = RecognizeDocument(document);
            
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored))
                .Returns(false);
            
            fileSender.SendFiles(new[] {file}, certificate)
                .SkippedFiles.Should().Contain(new File[] {file});
            
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored)).MustHaveHappened(1, Times.Exactly);
        }

        [Test]
        public void Skip_WhenNotRecognized()
        {
            var document = GenerateDocument(DocumentFormat.Version40, DateTime.Now);
            
            fileSender.SendFiles(new[] {file}, certificate)
                .SkippedFiles.Should().BeEquivalentTo(file);
            
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored)).MustNotHaveHappened();
        }

        [Test]
        public void IndependentlySend_WhenSeveralFilesAndSomeAreInvalid()
        {
            var documents = new[]
            {
                GenerateDocument(DocumentFormat.Version40, DateTime.Now),
                GenerateDocument(DocumentFormat.Invalid, DateTime.Now),
                GenerateDocument(DocumentFormat.Version31, DateTime.Now),
                GenerateDocument(DocumentFormat.Invalid, DateTime.Now)
            };
            var files = documents.Select(RecognizeDocument).ToArray();

            fileSender.SendFiles(files, certificate)
                .SkippedFiles.Should().BeEquivalentTo(files[1], files[3]);
            
            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored)).MustHaveHappened(2, Times.Exactly);
        }

        [Test]
        public void IndependentlySend_WhenSeveralFilesAndSomeCouldNotSend()
        {
            var documents = new[]
            {
                GenerateDocument(DocumentFormat.Version40, DateTime.Now),
                GenerateDocument(DocumentFormat.Version31, DateTime.Now),
                GenerateDocument(DocumentFormat.Version31, DateTime.Now),
                GenerateDocument(DocumentFormat.Version40, DateTime.Now)
            };
            var files = documents.Select(RecognizeDocument).ToArray();

            A.CallTo(() => sender.TrySend(A<byte[]>.Ignored)).ReturnsNextFromSequence(true, false, true, false);
            
            fileSender.SendFiles(files, certificate)
                .SkippedFiles.Should().BeEquivalentTo(files[1], files[3]);
            
        }

        private File RecognizeDocument(Document document)
        {
            var recognized = new File(document.Name, document.Content);
            A.CallTo(() => recognizer.TryRecognize(recognized, out document))
                .Returns(true);
            return recognized;
        }

        private byte[] GenerateFileContent()
        {
            var contentSize = random.Next(1, 10);
            var content = new byte[contentSize];
            random.NextBytes(content);
            return content;
        }

        private Document GenerateDocument(DocumentFormat format, DateTime created)
        {
            return new Document(file.Name, GenerateFileContent(), created, GetDescription(format));
        }
        
        private static string GetDescription(DocumentFormat value)
        {
            return value switch
            {
                DocumentFormat.Version31 => "3.1",
                DocumentFormat.Version40 => "4.0",
                DocumentFormat.Invalid => "2.2",
                _ => ""
            };
        }
        
        [Test]
        [Ignore("Not implemented")]
        public void CheckDescription()
        {
            Assert.AreEqual("3.1", DocumentFormat.Version31.ToString());
            Assert.AreEqual("4.0", DocumentFormat.Version40.ToString());
            Assert.AreEqual("2.2", DocumentFormat.Invalid.ToString());
        }
    }
}
