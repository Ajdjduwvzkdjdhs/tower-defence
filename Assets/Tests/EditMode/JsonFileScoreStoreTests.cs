using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using KakigoriTD.Core;
using KakigoriTD.Persistence;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// ランキングのローカル DB ファイル（JSON・ADR-34）。往復・未作成・破損時の退避を確認する。
    /// </summary>
    public class JsonFileScoreStoreTests
    {
        private string _dir;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kakigori-ranking-test-" + Path.GetRandomFileName());
            _path = Path.Combine(_dir, "sub", "ranking.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void Load_MissingFile_IsEmpty()
        {
            var store = new JsonFileScoreStore(_path);
            Assert.That(store.Load().Count, Is.EqualTo(0));
        }

        [Test]
        public void Save_ThenLoad_RoundTrips()
        {
            var store = new JsonFileScoreStore(_path);
            store.Save(new[]
            {
                new ScoreEntry(61.5f, 100, 12, 1700000000L),
                new ScoreEntry(95f, 100, 3, 1700000100L),
            });

            Assert.That(File.Exists(_path), "サブフォルダごと作られる");
            Assert.That(File.Exists(_path + ".tmp"), Is.False, "一時ファイルは残らない");

            var loaded = store.Load();
            Assert.That(loaded.Count, Is.EqualTo(2));
            Assert.That(loaded[0].ClearTimeSec, Is.EqualTo(61.5f));
            Assert.That(loaded[0].KillCount, Is.EqualTo(100));
            Assert.That(loaded[0].MaxCombo, Is.EqualTo(12));
            Assert.That(loaded[0].RecordedAtUnixSec, Is.EqualTo(1700000000L));
            Assert.That(loaded[1].ClearTimeSec, Is.EqualTo(95f));
        }

        [Test]
        public void Save_Overwrites_ExistingFile()
        {
            var store = new JsonFileScoreStore(_path);
            store.Save(new[] { new ScoreEntry(10f, 1, 1, 1) });
            store.Save(new[] { new ScoreEntry(20f, 2, 2, 2), new ScoreEntry(30f, 3, 3, 3) });

            var loaded = store.Load();
            Assert.That(loaded.Count, Is.EqualTo(2));
            Assert.That(loaded[0].ClearTimeSec, Is.EqualTo(20f));
        }

        [Test]
        public void Clear_DeletesFile_AndIsNoOpWhenMissing()
        {
            var store = new JsonFileScoreStore(_path);
            store.Clear(); // 未作成でも例外にしない

            store.Save(new[] { new ScoreEntry(10f, 1, 1, 1) });
            Assert.That(File.Exists(_path));
            store.Clear();
            Assert.That(File.Exists(_path), Is.False);
            Assert.That(store.Load().Count, Is.EqualTo(0));
        }

        [Test]
        public void Load_CorruptFile_QuarantinesAndStartsEmpty()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ this is not json");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Ranking\]"));

            var store = new JsonFileScoreStore(_path);
            Assert.That(store.Load().Count, Is.EqualTo(0));
            Assert.That(File.Exists(_path), Is.False, "壊れたファイルは退避される");
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(_path), "ranking.json.corrupt-*").Length, Is.EqualTo(1));
        }

        [Test]
        public void ResolvePath_DefaultsToPersistentDataPath()
        {
            var prev = System.Environment.GetEnvironmentVariable(JsonFileScoreStore.PathEnvVar);
            try
            {
                System.Environment.SetEnvironmentVariable(JsonFileScoreStore.PathEnvVar, null);
                Assert.That(JsonFileScoreStore.ResolvePath("/data", "ranking.json"),
                    Is.EqualTo(Path.Combine("/data", "ranking.json")));

                System.Environment.SetEnvironmentVariable(JsonFileScoreStore.PathEnvVar, "/usb/r.json");
                Assert.That(JsonFileScoreStore.ResolvePath("/data", "ranking.json"), Is.EqualTo("/usb/r.json"));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(JsonFileScoreStore.PathEnvVar, prev);
            }
        }

        [Test]
        public void Ctor_EmptyPath_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new JsonFileScoreStore(""));
        }
    }
}
