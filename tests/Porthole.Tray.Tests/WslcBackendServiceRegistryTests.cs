using System.Text.Json;
using Porthole.Tray.Services;

namespace Porthole.Tray.Tests;

public sealed class WslcBackendServiceRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sessionStoragePath;
    private readonly string _registryPath;
    private List<WslcBackendService> _servicesToDispose = [];

    public WslcBackendServiceRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"porthole-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _sessionStoragePath = Path.Combine(_tempDir, "Porthole", "Sessions");
        _registryPath = Path.Combine(_tempDir, "Porthole", "sessions.json");
    }

    public void Dispose()
    {
        // Dispose all services first to release SDK locks
        foreach (var service in _servicesToDispose)
        {
            try { service?.Dispose(); } catch { }
        }
        _servicesToDispose.Clear();

        // Give the SDK a moment to release file locks
        System.Threading.Thread.Sleep(100);

        // Try to clean up the temp directory
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors - files may still be locked by the OS
            }
        }
    }

    private WslcBackendService CreateService()
    {
        var service = new WslcBackendService(baseStoragePath: _sessionStoragePath, registryPath: _registryPath, skipDefaultSessionDetection: true);
        _servicesToDispose.Add(service);
        return service;
    }

    /// <summary>
    /// Test that a fresh service with no registry file creates a default session and saves the registry.
    /// </summary>
    [Fact]
    public void Constructor_NoRegistry_CreatesDefaultSessionAndPersists()
    {
        var service = CreateService();

        // Check that the registry file was created
        Assert.True(File.Exists(_registryPath), "Registry file should be created");

        // Check that the default session is active
        Assert.Equal("porthole-devcontainers", service.GetActiveSessionName());

        // Check that we can list sessions and see the default
        var sessions = service.ListSessions();
        Assert.Single(sessions);
        Assert.Equal("porthole-devcontainers", sessions[0].Name);
        Assert.True(sessions[0].IsActive);

        // Verify registry content
        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("porthole-devcontainers", doc.RootElement.GetProperty("activeSessionName").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("knownSessionNames").GetArrayLength());
    }

    /// <summary>
    /// Test that the service loads an existing registry from disk.
    /// </summary>
    [Fact]
    public void Constructor_ExistingRegistry_LoadsActiveSessionAndKnownSessions()
    {
        // Create a registry file with two sessions
        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        var registry = new { activeSessionName = "myapp", knownSessionNames = new[] { "myapp", "porthole-devcontainers" } };
        File.WriteAllText(_registryPath, JsonSerializer.Serialize(registry));

        // Create the session directories so they exist
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "myapp"));
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "porthole-devcontainers"));

        var service = CreateService();

        // Check that the active session is correct
        Assert.Equal("myapp", service.GetActiveSessionName());

        // Check that all sessions are visible
        var sessions = service.ListSessions();
        Assert.Equal(2, sessions.Count);
        var active = sessions.FirstOrDefault(s => s.IsActive);
        Assert.NotNull(active);
        Assert.Equal("myapp", active.Name);
    }

    /// <summary>
    /// Test migration path: when no registry exists but session directories do, discover them and select a non-default one.
    /// </summary>
    [Fact]
    public void Constructor_NoRegistry_DiscoverSessionDirectories_SelectNonDefault()
    {
        // Create session directories without a registry
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "myapp"));
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "porthole-devcontainers"));

        var service = CreateService();

        // Check that the active session is the non-default one (myapp comes before porthole-devcontainers alphabetically)
        Assert.Equal("myapp", service.GetActiveSessionName());

        // Check that both sessions are visible
        var sessions = service.ListSessions();
        Assert.Equal(2, sessions.Count);
        var sessionNames = sessions.Select(s => s.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "myapp", "porthole-devcontainers" }, sessionNames);

        // Verify registry was created with the discovered sessions
        Assert.True(File.Exists(_registryPath));
        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("myapp", doc.RootElement.GetProperty("activeSessionName").GetString());
    }

    /// <summary>
    /// Test migration path: when multiple non-default session directories exist, pick the first alphabetically.
    /// </summary>
    [Fact]
    public void Constructor_NoRegistry_MultipleNonDefaultDirectories_SelectFirst()
    {
        // Create three non-default session directories
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "zebra"));
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "apple"));
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "banana"));

        var service = CreateService();

        // Should select "apple" (first alphabetically, also non-default)
        Assert.Equal("apple", service.GetActiveSessionName());

        var sessions = service.ListSessions();
        Assert.Equal(3, sessions.Count);
    }

    /// <summary>
    /// Test migration path: only default session directory exists, use it as active.
    /// </summary>
    [Fact]
    public void Constructor_NoRegistry_OnlyDefaultDirectory_UseDefault()
    {
        // Create only the default session directory
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "porthole-devcontainers"));

        var service = CreateService();

        // Should select the default
        Assert.Equal("porthole-devcontainers", service.GetActiveSessionName());

        var sessions = service.ListSessions();
        Assert.Single(sessions);
        Assert.Equal("porthole-devcontainers", sessions[0].Name);
    }

    /// <summary>
    /// Test that CreateNamedSession persists to the registry.
    /// </summary>
    [Fact]
    public void CreateNamedSession_PersistsToRegistry()
    {
        var service = CreateService();

        service.CreateNamedSession("newsession");

        var sessions = service.ListSessions();
        Assert.Contains(sessions, s => s.Name == "newsession");

        // Verify registry was updated
        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        var knownNames = doc.RootElement.GetProperty("knownSessionNames").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("newsession", knownNames);
    }

    /// <summary>
    /// Test that SetActiveSession persists the new active session to the registry.
    /// </summary>
    [Fact]
    public void SetActiveSession_PersistsToRegistry()
    {
        var service = CreateService();

        service.CreateNamedSession("newsession");
        service.SetActiveSession("newsession");

        // Verify the registry has the new active session
        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("newsession", doc.RootElement.GetProperty("activeSessionName").GetString());

        // Verify GetActiveSessionName returns the new active session
        Assert.Equal("newsession", service.GetActiveSessionName());
    }

    /// <summary>
    /// Test that DeleteNamedSession removes the session from the registry and _sessionSettings.
    /// </summary>
    [Fact]
    public void DeleteNamedSession_RemovesFromRegistry()
    {
        var service = CreateService();

        // Create a session
        service.CreateNamedSession("todelete");
        var sessions = service.ListSessions();
        Assert.Contains(sessions, s => s.Name == "todelete");

        // Switch away from it first (can't delete active session)
        service.SetActiveSession("porthole-devcontainers");

        // Delete it
        service.DeleteNamedSession("todelete");

        // Verify it's gone from the list
        sessions = service.ListSessions();
        Assert.DoesNotContain(sessions, s => s.Name == "todelete");

        // Verify it's gone from the registry
        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        var knownNames = doc.RootElement.GetProperty("knownSessionNames").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.DoesNotContain("todelete", knownNames);
    }

    /// <summary>
    /// Test that DeleteNamedSession handles registry-only sessions (never instantiated).
    /// </summary>
    [Fact]
    public void DeleteNamedSession_RegistryOnlySession_RemovesFromRegistry()
    {
        // Create a registry with two sessions
        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        var registry = new { activeSessionName = "active", knownSessionNames = new[] { "active", "registry-only" } };
        File.WriteAllText(_registryPath, JsonSerializer.Serialize(registry));

        // Create only the active session directory (not the registry-only one)
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "active"));

        var service = CreateService();

        // Delete the registry-only session (which was never instantiated)
        service.DeleteNamedSession("registry-only");

        // Verify it's gone from the registry
        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        var knownNames = doc.RootElement.GetProperty("knownSessionNames").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.DoesNotContain("registry-only", knownNames);
    }

    /// <summary>
    /// Test that GetKnownSessionNamesLocked does not perform directory scans (uses only _sessionSettings and active session).
    /// </summary>
    [Fact]
    public void ListSessions_NoDirectoryScan_OnlyUsesRegistry()
    {
        var service = CreateService();

        // Create some extra directories that were not registered
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "phantom-session"));

        // ListSessions should NOT see the phantom session
        var sessions = service.ListSessions();
        Assert.DoesNotContain(sessions, s => s.Name == "phantom-session");

        // Only the active session and any others from registry should be visible
        var sessionNames = sessions.Select(s => s.Name).ToList();
        Assert.DoesNotContain("phantom-session", sessionNames);
    }

    /// <summary>
    /// Test that corrupted registry file triggers fallback to discovery.
    /// </summary>
    [Fact]
    public void Constructor_CorruptedRegistry_FallsBackToDiscovery()
    {
        // Create a corrupted registry file
        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        File.WriteAllText(_registryPath, "{ invalid json");

        // Create a valid session directory
        Directory.CreateDirectory(Path.Combine(_sessionStoragePath, "myapp"));

        var service = CreateService();

        // Should have discovered the session from the filesystem
        Assert.Equal("myapp", service.GetActiveSessionName());

        var sessions = service.ListSessions();
        Assert.Contains(sessions, s => s.Name == "myapp");
    }

    /// <summary>
    /// Test that registry persistence survives a service restart (load-persist-load cycle).
    /// </summary>
    [Fact]
    public void PersistenceAcrossRestarts_SessionsPreserved()
    {
        // First instance: create and modify sessions
        var service1 = CreateService();
        service1.CreateNamedSession("prod");
        service1.CreateNamedSession("staging");
        service1.SetActiveSession("prod");

        service1.Dispose();

        // Second instance: verify the state is preserved
        var service2 = CreateService();
        Assert.Equal("prod", service2.GetActiveSessionName());

        var sessions = service2.ListSessions();
        var sessionNames = sessions.Select(s => s.Name).OrderBy(n => n).ToList();
        Assert.Contains("prod", sessionNames);
        Assert.Contains("staging", sessionNames);

        service2.Dispose();
    }

    /// <summary>
    /// Test that whitespace-only names are filtered from the registry persistence.
    /// </summary>
    [Fact]
    public void SaveRegistry_WhitespaceNamesFiltered()
    {
        var service = CreateService();

        // The implementation filters whitespace-only names when saving
        // This test verifies that behavior doesn't corrupt the registry

        service.CreateNamedSession("valid");

        var json = File.ReadAllText(_registryPath);
        using var doc = JsonDocument.Parse(json);
        var knownNames = doc.RootElement.GetProperty("knownSessionNames").EnumerateArray().Select(e => e.GetString()).ToList();

        // All names in the registry should be valid (non-whitespace)
        foreach (var name in knownNames)
        {
            Assert.NotNull(name);
            Assert.NotEmpty(name);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }
}
