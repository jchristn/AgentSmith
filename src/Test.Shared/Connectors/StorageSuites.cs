namespace Test.Shared.Connectors
{
    using System.Collections.Generic;
    using Hugo.Connectors.Storage;
    using Test.Shared.Connectors.Storage;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Storage connector family. Unit suites always run; testbed suites run when HUGO_TESTBED=1.
    /// </summary>
    public static class StorageSuites
    {
        /// <summary>
        /// Build the suites: <c>connectors.storage</c> (offline) and <c>testbed.storage.s3</c>,
        /// <c>testbed.storage.azure-blob</c>, <c>testbed.storage.gcs</c> (docker/testbed/storage).
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            suites.Add(StorageUnitCases.Build());
            suites.Add(new StorageTestbedCases(StorageProviderEnum.AmazonS3).Build());
            suites.Add(new StorageTestbedCases(StorageProviderEnum.AzureBlob).Build());
            suites.Add(new StorageTestbedCases(StorageProviderEnum.GoogleCloud).Build());
            return suites;
        }
    }
}
