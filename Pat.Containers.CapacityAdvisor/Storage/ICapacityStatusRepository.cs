namespace Pat.Containers.CapacityAdvisor.Storage;

public interface ICapacityStatusRepository
{
    Task<CapacityStatusEntity?> GetAsync(string partitionKey, CancellationToken cancellationToken);
    Task UpsertAsync(CapacityStatusEntity entity, CancellationToken cancellationToken);
}