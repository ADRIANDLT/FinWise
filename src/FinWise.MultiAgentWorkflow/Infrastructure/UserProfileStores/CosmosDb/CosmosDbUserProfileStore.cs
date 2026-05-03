using FinWise.MultiAgentWorkflow.DomainModel;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net;

namespace FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores.CosmosDb;

/// <summary>
/// CosmosDB implementation of user profile storage.
/// Uses Azure CosmosDB NoSQL API for persistent storage.
/// </summary>
public class CosmosDbUserProfileStore : IUserProfileStore, IAsyncDisposable
{
    private readonly CosmosClient _client;
    private readonly CosmosDbOptions _options;
    private Container? _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public CosmosDbUserProfileStore(CosmosClient client, IOptions<CosmosDbOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Ensures the database and container exist, creating them if necessary.
    /// Thread-safe initialization that runs only once.
    /// </summary>
    private async Task<Container> GetContainerAsync()
    {
        if (_initialized && _container != null)
        {
            return _container;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized && _container != null)
            {
                return _container;
            }

            Log.Information("Initializing CosmosDB database '{Database}' and container '{Container}'",
                _options.DatabaseName, _options.ContainerName);

            // Do not specify throughput — Serverless accounts don't support it,
            // and the emulator works fine without it.
            var database = await _client.CreateDatabaseIfNotExistsAsync(
                id: _options.DatabaseName
            );

            _container = await database.Database.CreateContainerIfNotExistsAsync(
                id: _options.ContainerName,
                partitionKeyPath: "/userId"
            );

            _initialized = true;
            Log.Information("CosmosDB initialization complete");

            return _container;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<UserProfile?> GetProfileAsync(string userId)
    {
        try
        {
            var container = await GetContainerAsync();
            var documentId = UserProfileDocument.EmailToDocumentId(userId);
            var response = await container.ReadItemAsync<UserProfileDocument>(
                id: documentId,
                partitionKey: new PartitionKey(userId)
            );

            Log.Debug("CosmosDbProfileStore.GetProfileAsync({UserId}): FOUND (RU: {RequestCharge})",
                userId, response.RequestCharge);

            return response.Resource.ToModel();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Log.Debug("CosmosDbProfileStore.GetProfileAsync({UserId}): NOT FOUND", userId);
            return null;
        }
        catch (CosmosException ex)
        {
            Log.Error(ex, "CosmosDbProfileStore.GetProfileAsync({UserId}): Error - {StatusCode}",
                userId, ex.StatusCode);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SetProfileAsync(string userId, UserProfile profile)
    {
        try
        {
            var container = await GetContainerAsync();

            // Fetch existing document to preserve fields not being updated
            var documentId = UserProfileDocument.EmailToDocumentId(userId);
            UserProfileDocument? existingDocument = null;
            try
            {
                var existingResponse = await container.ReadItemAsync<UserProfileDocument>(
                    id: documentId,
                    partitionKey: new PartitionKey(userId)
                );
                existingDocument = existingResponse.Resource;
                Log.Debug("CosmosDbProfileStore.SetProfileAsync({UserId}): Found existing profile (RU: {RequestCharge})",
                    userId, existingResponse.RequestCharge);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                Log.Debug("CosmosDbProfileStore.SetProfileAsync({UserId}): No existing profile, creating new", userId);
            }

            // Merge: only update fields that are non-null in the incoming model
            var document = UserProfileDocument.FromModelWithMerge(profile, existingDocument);

            var response = await container.UpsertItemAsync(
                item: document,
                partitionKey: new PartitionKey(userId)
            );

            Log.Debug("CosmosDbProfileStore.SetProfileAsync({UserId}): Saved (RU: {RequestCharge})",
                userId, response.RequestCharge);
        }
        catch (CosmosException ex)
        {
            Log.Error(ex, "CosmosDbProfileStore.SetProfileAsync({UserId}): Error - {StatusCode}",
                userId, ex.StatusCode);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HasProfileAsync(string userId)
    {
        try
        {
            var container = await GetContainerAsync();
            var documentId = UserProfileDocument.EmailToDocumentId(userId);
            await container.ReadItemAsync<UserProfileDocument>(
                id: documentId,
                partitionKey: new PartitionKey(userId)
            );

            Log.Debug("CosmosDbProfileStore.HasProfileAsync({UserId}): true", userId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Log.Debug("CosmosDbProfileStore.HasProfileAsync({UserId}): false", userId);
            return false;
        }
        catch (CosmosException ex)
        {
            Log.Error(ex, "CosmosDbProfileStore.HasProfileAsync({UserId}): Error - {StatusCode}",
                userId, ex.StatusCode);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteProfileAsync(string userId)
    {
        try
        {
            var container = await GetContainerAsync();
            var documentId = UserProfileDocument.EmailToDocumentId(userId);
            var response = await container.DeleteItemAsync<UserProfileDocument>(
                id: documentId,
                partitionKey: new PartitionKey(userId)
            );

            Log.Debug("CosmosDbProfileStore.DeleteProfileAsync({UserId}): Deleted (RU: {RequestCharge})",
                userId, response.RequestCharge);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Log.Debug("CosmosDbProfileStore.DeleteProfileAsync({UserId}): Not found (no-op)", userId);
        }
        catch (CosmosException ex)
        {
            Log.Error(ex, "CosmosDbProfileStore.DeleteProfileAsync({UserId}): Error - {StatusCode}",
                userId, ex.StatusCode);
            throw;
        }
    }

    /// <summary>
    /// Disposes the CosmosDB client.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}