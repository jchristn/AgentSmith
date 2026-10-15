namespace Test.Shared.Connectors
{
    using System.Collections.Generic;
    using Test.Shared.Connectors.Aws;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the AWS connector. Unit and stub suites always run; the testbed suite runs when HUGO_TESTBED=1.
    /// </summary>
    public static class AwsSuites
    {
        /// <summary>
        /// Build the suites: <c>connectors.aws</c> (offline), <c>connectors.aws.stubs</c> (in-process recorded-response stub
        /// for RDS, ELBv2, ECS, Auto Scaling, STS, and EC2 actions), and <c>testbed.aws</c> (LocalStack, docker/testbed/aws).
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            suites.Add(AwsUnitCases.Build());
            suites.Add(new AwsStubCases().Build());
            suites.Add(new AwsTestbedCases().Build());
            return suites;
        }
    }
}
