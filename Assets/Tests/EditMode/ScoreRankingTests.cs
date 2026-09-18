using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// クリアタイムのランキング（ADR-34）。昇順・同タイム先着優先・容量超過はランク外・失敗は記録しない。
    /// </summary>
    public class ScoreRankingTests
    {
        private static ScoreEntry E(float sec, long at = 0) => new ScoreEntry(sec, 100, 5, at);

        [Test]
        public void Insert_KeepsAscendingOrderAndReturnsRank()
        {
            var r = new ScoreRanking(10);
            Assert.That(r.Insert(E(90f)), Is.EqualTo(1));
            Assert.That(r.Insert(E(60f)), Is.EqualTo(1));
            Assert.That(r.Insert(E(120f)), Is.EqualTo(3));
            Assert.That(r.Insert(E(75f)), Is.EqualTo(2));

            Assert.That(r.Count, Is.EqualTo(4));
            Assert.That(r.Entries[0].ClearTimeSec, Is.EqualTo(60f));
            Assert.That(r.Entries[1].ClearTimeSec, Is.EqualTo(75f));
            Assert.That(r.Entries[2].ClearTimeSec, Is.EqualTo(90f));
            Assert.That(r.Entries[3].ClearTimeSec, Is.EqualTo(120f));
        }

        [Test]
        public void Insert_SameTime_EarlierStaysAhead()
        {
            var r = new ScoreRanking(10);
            r.Insert(E(60f, at: 1));
            int rank = r.Insert(E(60f, at: 2));
            Assert.That(rank, Is.EqualTo(2));
            Assert.That(r.Entries[0].RecordedAtUnixSec, Is.EqualTo(1));
            Assert.That(r.Entries[1].RecordedAtUnixSec, Is.EqualTo(2));
        }

        [Test]
        public void Insert_OverCapacity_DropsLastAndReturnsUnrankedWhenNotInTop()
        {
            var r = new ScoreRanking(3);
            r.Insert(E(30f));
            r.Insert(E(40f));
            r.Insert(E(50f));

            Assert.That(r.Insert(E(60f)), Is.EqualTo(ScoreRanking.Unranked), "容量に入らなければランク外");
            Assert.That(r.Count, Is.EqualTo(3));

            Assert.That(r.Insert(E(45f)), Is.EqualTo(3), "3 位に割り込んだら旧 3 位が落ちる");
            Assert.That(r.Count, Is.EqualTo(3));
            Assert.That(r.Entries[2].ClearTimeSec, Is.EqualTo(45f));
        }

        [Test]
        public void Insert_SameTimeAtCapacityEdge_IsUnranked()
        {
            var r = new ScoreRanking(2);
            r.Insert(E(30f));
            r.Insert(E(40f));
            Assert.That(r.Insert(E(40f)), Is.EqualTo(ScoreRanking.Unranked), "同タイムは先着優先なので末尾と並んでも入らない");
        }

        [Test]
        public void TryRecord_Loss_IsNotRecorded()
        {
            var r = new ScoreRanking(10);
            var loss = new StageResult(isWin: false, clearTimeSec: 50f, killCount: 62, targetKillCount: 100, maxCombo: 4);
            Assert.That(r.TryRecord(loss, 0), Is.EqualTo(ScoreRanking.Unranked));
            Assert.That(r.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryRecord_Win_CarriesResultFields()
        {
            var r = new ScoreRanking(10);
            var win = new StageResult(isWin: true, clearTimeSec: 102.4f, killCount: 100, targetKillCount: 100, maxCombo: 17);
            Assert.That(r.TryRecord(win, 1234), Is.EqualTo(1));
            var e = r.Entries[0];
            Assert.That(e.ClearTimeSec, Is.EqualTo(102.4f));
            Assert.That(e.KillCount, Is.EqualTo(100));
            Assert.That(e.MaxCombo, Is.EqualTo(17));
            Assert.That(e.RecordedAtUnixSec, Is.EqualTo(1234));
        }

        [Test]
        public void FromEntries_SortsUnorderedInputAndTrimsToCapacity()
        {
            var r = ScoreRanking.FromEntries(new[] { E(90f, 3), E(30f, 1), E(60f, 2), E(60f, 0), E(120f, 4) }, capacity: 4);
            Assert.That(r.Count, Is.EqualTo(4));
            Assert.That(r.Entries[0].ClearTimeSec, Is.EqualTo(30f));
            Assert.That(r.Entries[1].RecordedAtUnixSec, Is.EqualTo(0), "同タイムは記録時刻が古い方が上");
            Assert.That(r.Entries[2].RecordedAtUnixSec, Is.EqualTo(2));
            Assert.That(r.Entries[3].ClearTimeSec, Is.EqualTo(90f));
        }

        [Test]
        public void FromEntries_Null_IsEmpty()
        {
            var r = ScoreRanking.FromEntries(null, 5);
            Assert.That(r.Count, Is.EqualTo(0));
        }

        [Test]
        public void Top_ClampsToCount()
        {
            var r = new ScoreRanking(10);
            r.Insert(E(10f));
            r.Insert(E(20f));
            Assert.That(r.Top(5).Count, Is.EqualTo(2));
            Assert.That(r.Top(1)[0].ClearTimeSec, Is.EqualTo(10f));
            Assert.That(r.Top(-1).Count, Is.EqualTo(0));
        }

        [Test]
        public void Capacity_MinimumIsOne()
        {
            Assert.That(new ScoreRanking(0).Capacity, Is.EqualTo(1));
        }

        [Test]
        public void ScoreEntry_NegativeTime_ClampsToZero()
        {
            Assert.That(new ScoreEntry(-5f, 0, 0, 0).ClearTimeSec, Is.EqualTo(0f));
        }
    }
}
