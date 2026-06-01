using FoToolbox.Core.Auth;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace FoToolbox.Tests;

public sealed class MsalTokenCacheStoreTests
{
    [Fact]
    public void InMemory_RoundTripsAndReturnsNullForUnknownKey()
    {
        var store = new InMemoryMsalTokenCacheStore();
        Assert.Null(store.Load("missing"));

        var data = Encoding.UTF8.GetBytes("cache-blob");
        store.Save("k1", data);
        Assert.Equal(data, store.Load("k1"));
    }

    [Fact]
    public void DpapiFile_RoundTripsAcrossInstances()
    {
        var dir = Directory.CreateTempSubdirectory("msal-cache").FullName;
        try
        {
            var data = Encoding.UTF8.GetBytes("the-msal-cache-bytes");
            new DpapiFileMsalTokenCacheStore(dir).Save("env-1", data);

            // A fresh instance (simulating a restart) reads the persisted, encrypted blob back.
            var loaded = new DpapiFileMsalTokenCacheStore(dir).Load("env-1");

            Assert.Equal(data, loaded);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DpapiFile_ReturnsNullForUnknownKey()
    {
        var dir = Directory.CreateTempSubdirectory("msal-cache").FullName;
        try
        {
            Assert.Null(new DpapiFileMsalTokenCacheStore(dir).Load("never-saved"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DpapiFile_KeysAreIsolated()
    {
        var dir = Directory.CreateTempSubdirectory("msal-cache").FullName;
        try
        {
            var store = new DpapiFileMsalTokenCacheStore(dir);
            store.Save("env-a", Encoding.UTF8.GetBytes("aaaa"));
            store.Save("env-b", Encoding.UTF8.GetBytes("bbbb"));

            Assert.Equal("aaaa", Encoding.UTF8.GetString(store.Load("env-a")!));
            Assert.Equal("bbbb", Encoding.UTF8.GetString(store.Load("env-b")!));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DpapiFile_ReturnsNullForCorruptedFile()
    {
        var dir = Directory.CreateTempSubdirectory("msal-cache").FullName;
        try
        {
            var store = new DpapiFileMsalTokenCacheStore(dir);
            store.Save("env-1", Encoding.UTF8.GetBytes("good"));
            // Corrupt every persisted cache file.
            foreach (var f in Directory.GetFiles(dir))
            {
                File.WriteAllBytes(f, new byte[] { 1, 2, 3, 4, 5 });
            }

            Assert.Null(store.Load("env-1"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
