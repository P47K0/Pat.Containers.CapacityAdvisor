using Azure.Data.Tables;

namespace Pat.Containers.CapacityAdvisor.Storage
{
    public sealed class TableStorageInitializer : IHostedService
    {
        private readonly TableClient _tableClient;

        public TableStorageInitializer(TableClient tableClient)
        {
            _tableClient = tableClient;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _tableClient.CreateIfNotExistsAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
