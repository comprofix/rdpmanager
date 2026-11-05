using System.Text.Json.Nodes;
using RDPManager.Core.Model;
using RDPManager.Core.Settings;

namespace RDPManager.Core.Tests;

public class SettingsSchemaTests
{
    [Fact]
    public void Keys_AreUnique()
    {
        var keys = SettingsSchema.All.Select(d => d.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void EveryDefault_IsValidForItsSetting()
    {
        Assert.All(SettingsSchema.All, d => Assert.True(d.IsValid(d.Default), $"Default for {d.Key} is invalid."));
    }

    [Fact]
    public void Groups_CoverTheFirstVersionTable()
    {
        Assert.Equal(
            ["Basics", "Display", "Security", "RD Gateway", "Redirection", "Session"],
            SettingsSchema.All.Select(d => d.Group).Distinct());
    }

    [Theory]
    [InlineData("host")]
    [InlineData("description")]
    public void PerNodeSettings_AreNotInheritable(string key)
    {
        Assert.False(SettingsSchema.Get(key).Inheritable);
    }

    [Fact]
    public void Host_AppliesOnlyToConnections()
    {
        var host = SettingsSchema.Get("host");
        Assert.True(host.AppliesTo(NodeType.Connection));
        Assert.False(host.AppliesTo(NodeType.Folder));
    }

    [Fact]
    public void Secrets_AreStoredAsMarkers()
    {
        Assert.Equal("passwordStored", SettingsSchema.Get("password").StorageKey);
        Assert.Equal("gateway.passwordStored", SettingsSchema.Get("gateway.password").StorageKey);
        Assert.Equal("port", SettingsSchema.Get("port").StorageKey);
    }

    [Fact]
    public void IsValid_ChecksTypeAndChoices()
    {
        Assert.True(SettingsSchema.Get("port").IsValid(JsonValue.Create(3390)));
        Assert.False(SettingsSchema.Get("port").IsValid(JsonValue.Create("3390")));
        Assert.True(SettingsSchema.Get("gateway.usage").IsValid(JsonValue.Create("always")));
        Assert.False(SettingsSchema.Get("gateway.usage").IsValid(JsonValue.Create("sometimes")));
    }

    [Fact]
    public void Get_ThrowsForUnknownKey()
    {
        Assert.Null(SettingsSchema.Find("nope"));
        Assert.Throws<ArgumentException>(() => SettingsSchema.Get("nope"));
    }
}
