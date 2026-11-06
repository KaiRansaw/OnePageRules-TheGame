using System.Net;
using FdgRaylib.Network;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The Host screen's Tailscale row reads Tailscale's address block, 100.64.0.0/10, and nothing else.</summary>
[TestFixture]
public class TailscaleInfoTests
{
    [TestCase("100.64.0.1", true)]
    [TestCase("100.101.102.103", true)]
    [TestCase("100.127.255.254", true)]
    [TestCase("100.63.255.255", false)]
    [TestCase("100.128.0.1", false)]
    [TestCase("192.168.1.20", false)]
    [TestCase("10.0.0.5", false)]
    public void TheTailscaleBlock(string address, bool expected) =>
        Assert.That(TailscaleInfo.IsTailscaleAddress(IPAddress.Parse(address)), Is.EqualTo(expected));

    [Test]
    public void IPv6IsNeverReported() =>
        Assert.That(TailscaleInfo.IsTailscaleAddress(IPAddress.Parse("fd7a:115c:a1e0::1")), Is.False);

    [Test]
    public void TheFullName_ComesFromTailscaleStatus()
    {
        Assert.That(TailscaleInfo.ParseDnsName("{\"Self\":{\"DNSName\":\"kais-pc.tail1a2b.ts.net.\"}}"), Is.EqualTo("kais-pc.tail1a2b.ts.net"));
        Assert.That(TailscaleInfo.ParseDnsName("{\"Self\":{}}"), Is.Null);
        Assert.That(TailscaleInfo.ParseDnsName("not json"), Is.Null);
    }

    [Test]
    public void TheLikelyName_IsTheComputersNameInLowerCase() =>
        Assert.That(TailscaleInfo.LikelyName(), Is.EqualTo(Environment.MachineName.ToLowerInvariant()));
}
