﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.SDK.EmulatorTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Internal;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class CosmosDatabaseTests
    {
        protected CosmosClient cosmosClient = null;
        protected CancellationTokenSource cancellationTokenSource = null;
        protected CancellationToken cancellationToken;

        [TestInitialize]
        public void TestInit()
        {
            this.cancellationTokenSource = new CancellationTokenSource();
            this.cancellationToken = this.cancellationTokenSource.Token;

            this.cosmosClient = TestCommon.CreateCosmosClient();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (this.cosmosClient == null)
            {
                return;
            }

            this.cancellationTokenSource?.Cancel();
            this.cosmosClient.Dispose();
        }

        [TestMethod]
        public async Task DatabaseContractTest()
        {
            DatabaseResponse response = await this.CreateDatabaseHelper();
            Assert.IsNotNull(response);
            Assert.IsTrue(response.RequestCharge > 0);
            Assert.IsNotNull(response.Headers);
            Assert.IsNotNull(response.Headers.ActivityId);

            CosmosDatabaseSettings databaseSettings = response.Resource;
            Assert.IsNotNull(databaseSettings.Id);
            Assert.IsNotNull(databaseSettings.ResourceId);
            Assert.IsNotNull(databaseSettings.ETag);
            Assert.IsTrue(databaseSettings.LastModified.HasValue);
            Assert.IsTrue(databaseSettings.LastModified.Value > new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), databaseSettings.LastModified.Value.ToString());

            CosmosDatabaseCore databaseCore = response.Database as CosmosDatabaseCore;
            Assert.IsNotNull(databaseCore);
            Assert.IsNotNull(databaseCore.LinkUri);
            Assert.IsFalse(databaseCore.LinkUri.ToString().StartsWith("/"));

            response = await response.Database.DeleteAsync(cancellationToken: this.cancellationToken);
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        }

        [TestMethod]
        public async Task CreateDropDatabase()
        {
            DatabaseResponse response = await this.CreateDatabaseHelper();
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

            response = await response.Database.DeleteAsync(cancellationToken: this.cancellationToken);
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        }

        [TestMethod]
        public async Task StreamCrudTestAsync()
        {
            CosmosDatabase database = await this.CreateDatabaseStreamHelper();

            using (CosmosResponseMessage response = await database.ReadAsStreamAsync())
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.IsNotNull(response.Headers);
                Assert.IsTrue(response.Headers.RequestCharge > 0);
            }

            using (CosmosResponseMessage response = await database.DeleteAsStreamAsync())
            {
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
                Assert.IsNotNull(response.Headers);
                Assert.IsTrue(response.Headers.RequestCharge > 0);
            }
        }

        [TestMethod]
        public async Task StreamCreateConflictTestAsync()
        {
            CosmosDatabaseSettings databaseSettings = new CosmosDatabaseSettings()
            {
                Id = Guid.NewGuid().ToString()
            };
            
            using (CosmosResponseMessage response = await this.cosmosClient.CreateDatabaseAsStreamAsync(databaseSettings))
            {
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
                Assert.IsNotNull(response.Headers);
                Assert.IsTrue(response.Headers.RequestCharge > 0);
            }

            // Stream operations do not throw exceptions.
            using (CosmosResponseMessage response = await this.cosmosClient.CreateDatabaseAsStreamAsync(databaseSettings))
            {
                Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
                Assert.IsNotNull(response.Headers);
                Assert.IsTrue(response.Headers.RequestCharge > 0);
            }

            using (CosmosResponseMessage response = await this.cosmosClient.GetDatabase(databaseSettings.Id).DeleteAsStreamAsync())
            {
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
                Assert.IsNotNull(response.Headers);
                Assert.IsTrue(response.Headers.RequestCharge > 0);
            }
        }

        [TestMethod]
        public async Task CreateConflict()
        {
            DatabaseResponse response = await this.CreateDatabaseHelper();
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

            try
            {
                response = await this.CreateDatabaseHelper(response.Resource.Id);
                Assert.Fail($"Unexpected success status code {response.StatusCode}");
            }
            catch (CosmosException hre)
            {
                DefaultTrace.TraceInformation(hre.ToString());
            }

            response = await response.Database.DeleteAsync(cancellationToken: this.cancellationToken);
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        }

        [TestMethod]
        public async Task ImplicitConversion()
        {
            string databaseName = Guid.NewGuid().ToString();

            DatabaseResponse cosmosDatabaseResponse = await this.cosmosClient.GetDatabase(databaseName).ReadAsync(cancellationToken: this.cancellationToken);
            CosmosDatabase cosmosDatabase = cosmosDatabaseResponse;
            CosmosDatabaseSettings cosmosDatabaseSettings = cosmosDatabaseResponse;
            Assert.IsNotNull(cosmosDatabase);
            Assert.IsNull(cosmosDatabaseSettings);

            cosmosDatabaseResponse = await this.CreateDatabaseHelper();
            cosmosDatabase = cosmosDatabaseResponse;
            cosmosDatabaseSettings = cosmosDatabaseResponse;
            Assert.IsNotNull(cosmosDatabase);
            Assert.IsNotNull(cosmosDatabaseSettings);

            cosmosDatabaseResponse = await cosmosDatabase.DeleteAsync(cancellationToken: this.cancellationToken);
            cosmosDatabase = cosmosDatabaseResponse;
            cosmosDatabaseSettings = cosmosDatabaseResponse;
            Assert.IsNotNull(cosmosDatabase);
            Assert.IsNull(cosmosDatabaseSettings);
        }

        [TestMethod]
        public async Task DropNonExistingDatabase()
        {
            DatabaseResponse response = await this.cosmosClient.GetDatabase(Guid.NewGuid().ToString()).DeleteAsync(cancellationToken: this.cancellationToken);

            string activityId = response.ActivityId;
            double? ru = response.RequestCharge;
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [TestMethod]
        public async Task ReadDatabase()
        {
            DatabaseResponse createResponse = await this.CreateDatabaseHelper();
            DatabaseResponse readResponse = await createResponse.Database.ReadAsync(cancellationToken: this.cancellationToken);

            Assert.AreEqual(createResponse.Database.Id, readResponse.Database.Id);
            Assert.AreEqual(createResponse.Resource.Id, readResponse.Resource.Id);
            Assert.AreNotEqual(createResponse.ActivityId, readResponse.ActivityId);
            ValidateHeaders(readResponse);
            await createResponse.Database.DeleteAsync(cancellationToken: this.cancellationToken);
        }

        [TestMethod]
        public async Task CreateIfNotExists()
        {
            DatabaseResponse createResponse = await this.CreateDatabaseHelper();
            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

            createResponse = await this.CreateDatabaseHelper(createResponse.Resource.Id, databaseExists: true);
            Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        }

        [TestMethod]
        public async Task NoThroughputTests()
        {
            string databaseId = Guid.NewGuid().ToString();
            DatabaseResponse createResponse = await this.CreateDatabaseHelper(databaseId, databaseExists: false);
            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

            CosmosDatabase cosmosDatabase = createResponse;
            int? readThroughput = await cosmosDatabase.ReadProvisionedThroughputAsync();
            Assert.IsNull(readThroughput);

            await cosmosDatabase.DeleteAsync();
        }

        [TestMethod]
        public async Task SharedThroughputTests()
        {
            string databaseId = Guid.NewGuid().ToString();
            int throughput = 10000;
            DatabaseResponse createResponse = await this.CreateDatabaseHelper(databaseId, databaseExists: false, throughput: throughput);
            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

            CosmosDatabase cosmosDatabase = createResponse;
            int? readThroughput = await cosmosDatabase.ReadProvisionedThroughputAsync();
            Assert.AreEqual(throughput, readThroughput);

            string containerId = Guid.NewGuid().ToString();
            string partitionPath = "/users";
            ContainerResponse containerResponse = await cosmosDatabase.CreateContainerAsync(containerId, partitionPath);
            Assert.AreEqual(HttpStatusCode.Created, containerResponse.StatusCode);

            CosmosContainer container = containerResponse;
            readThroughput = await container.ReadProvisionedThroughputAsync();
            Assert.IsNull(readThroughput);

            await container.DeleteAsync();
            await cosmosDatabase.DeleteAsync();
        }

        [TestMethod]
        public async Task DatabaseIterator()
        {
            List<CosmosDatabase> deleteList = new List<CosmosDatabase>();
            HashSet<string> databaseIds = new HashSet<string>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    DatabaseResponse createResponse = await this.CreateDatabaseHelper();
                    deleteList.Add(createResponse.Database);
                    databaseIds.Add(createResponse.Resource.Id);
                }

                FeedIterator<CosmosDatabaseSettings> feedIterator =
                    this.cosmosClient.GetDatabasesIterator();
                while (feedIterator.HasMoreResults)
                {
                    FeedResponse<CosmosDatabaseSettings> iterator =
                        await feedIterator.FetchNextSetAsync(this.cancellationToken);
                    foreach (CosmosDatabaseSettings databaseSettings in iterator)
                    {
                        if (databaseIds.Contains(databaseSettings.Id))
                        {
                            databaseIds.Remove(databaseSettings.Id);
                        }
                    }
                }
            }
            finally
            {
                foreach (CosmosDatabase database in deleteList)
                {
                    await database.DeleteAsync(cancellationToken: this.cancellationToken);
                }
            }

            Assert.AreEqual(0, databaseIds.Count);
        }

        private Task<DatabaseResponse> CreateDatabaseHelper()
        {
            return this.CreateDatabaseHelper(Guid.NewGuid().ToString(), databaseExists: false);
        }

        private async Task<DatabaseResponse> CreateDatabaseHelper(
            string databaseId,
            int? throughput = null,
            bool databaseExists = false)
        {
            DatabaseResponse response = null;
            if (databaseExists)
            {
                response = await this.cosmosClient.CreateDatabaseIfNotExistsAsync(
                    databaseId,
                    throughput,
                    cancellationToken: this.cancellationToken);
            }
            else
            {
                response = await this.cosmosClient.CreateDatabaseAsync(
                    databaseId,
                    throughput,
                    cancellationToken: this.cancellationToken);
            }

            Assert.IsNotNull(response);
            Assert.IsNotNull(response.Database);
            Assert.IsNotNull(response.Resource);
            Assert.AreEqual(databaseId, response.Resource.Id);
            Assert.AreEqual(databaseId, response.Database.Id);
            ValidateHeaders(response);

            Assert.IsTrue(response.StatusCode == HttpStatusCode.OK || (response.StatusCode == HttpStatusCode.Created && !databaseExists));

            return response;
        }

        private async Task<CosmosDatabase> CreateDatabaseStreamHelper(
            string databaseId = null,
            int? throughput = null,
            bool databaseExists = false)
        {
            if (string.IsNullOrEmpty(databaseId))
            {
                databaseId = Guid.NewGuid().ToString();
            }

            CosmosDatabaseSettings databaseSettings = new CosmosDatabaseSettings() { Id = databaseId };
            CosmosResponseMessage response = await this.cosmosClient.CreateDatabaseAsStreamAsync(
                databaseSettings,
                throughput: 400);

            Assert.IsNotNull(response);
            Assert.IsNotNull(response.Headers.RequestCharge);
            Assert.IsNotNull(response.Headers.ActivityId);

            Assert.IsTrue(response.StatusCode == HttpStatusCode.OK || (response.StatusCode == HttpStatusCode.Created && !databaseExists));

            return this.cosmosClient.GetDatabase(databaseId);
        }

        private void ValidateHeaders(DatabaseResponse cosmosDatabaseResponse)
        {
            Assert.IsNotNull(cosmosDatabaseResponse.MaxResourceQuota);
            Assert.IsNotNull(cosmosDatabaseResponse.CurrentResourceQuotaUsage);
        }
    }
}
