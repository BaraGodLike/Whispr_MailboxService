using Application;

namespace UnitTests.Mailboxes;

[TestClass]
public sealed class MailboxPolicyTests
{
    [TestMethod]
    public void BuildSchedule_ReturnsExpectedDays()
    {
        var today = new DateOnly(2026, 4, 25);

        var schedule = MailboxPolicy.BuildSchedule(today);

        Assert.AreEqual(today, schedule.Today);
        Assert.AreEqual(new DateOnly(2026, 5, 1), schedule.CurrentExpiresDay);
        Assert.AreEqual(new DateOnly(2026, 5, 2), schedule.NextExpiresDay);
        Assert.AreEqual(new DateOnly(2026, 4, 24), schedule.ExpiredPartitionDay);
    }

    [TestMethod]
    public void BuildSchedule_HandlesYearBoundary()
    {
        var today = new DateOnly(2026, 12, 29);

        var schedule = MailboxPolicy.BuildSchedule(today);

        Assert.AreEqual(new DateOnly(2027, 1, 4), schedule.CurrentExpiresDay);
        Assert.AreEqual(new DateOnly(2027, 1, 5), schedule.NextExpiresDay);
        Assert.AreEqual(new DateOnly(2026, 12, 28), schedule.ExpiredPartitionDay);
    }

    [TestMethod]
    public void BuildSchedule_HandlesLeapYear()
    {
        var today = new DateOnly(2024, 2, 27);

        var schedule = MailboxPolicy.BuildSchedule(today);

        Assert.AreEqual(new DateOnly(2024, 3, 4), schedule.CurrentExpiresDay);
        Assert.AreEqual(new DateOnly(2024, 3, 5), schedule.NextExpiresDay);
        Assert.AreEqual(new DateOnly(2024, 2, 26), schedule.ExpiredPartitionDay);
    }

    [TestMethod]
    public void GetClientRefreshAfterUtc_ReturnsSixDaysBeforeExpirationAtUtcMidnight()
    {
        var expiresDay = new DateOnly(2026, 5, 1);

        var refreshAfter = MailboxPolicy.GetClientRefreshAfterUtc(expiresDay);

        Assert.AreEqual(new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc), refreshAfter);
    }

    [TestMethod]
    [DataRow(2026, 4, 30, 2026, 5, 1, true)]
    [DataRow(2026, 5, 1, 2026, 5, 1, false)]
    [DataRow(2026, 5, 2, 2026, 5, 1, false)]
    public void IsOwnerMappingActive_ReturnsExpectedResult(
        int todayYear,
        int todayMonth,
        int todayDay,
        int expiresYear,
        int expiresMonth,
        int expiresDay,
        bool expected)
    {
        var today = new DateOnly(todayYear, todayMonth, todayDay);
        var expires = new DateOnly(expiresYear, expiresMonth, expiresDay);

        var isActive = MailboxPolicy.IsOwnerMappingActive(today, expires);

        Assert.AreEqual(expected, isActive);
    }
}
