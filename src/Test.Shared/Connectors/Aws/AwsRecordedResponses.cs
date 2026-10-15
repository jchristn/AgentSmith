namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Recorded AWS API response shapes replayed by <see cref="AwsStubServer"/> for the services LocalStack community does
    /// not emulate (RDS, ELBv2, Auto Scaling, ECS) plus STS and the EC2 instance actions, so every tool runs offline
    /// through the real AWS SDK serializers. Identifiers are fictitious; the account is 123456789012.
    /// </summary>
    public static class AwsRecordedResponses
    {
        #region Public-Members

        /// <summary>
        /// Account reported by the stub's sts:GetCallerIdentity.
        /// </summary>
        public const string Account = "123456789012";

        /// <summary>
        /// Access key ID returned by the stub's sts:AssumeRole.
        /// </summary>
        public const string AssumedAccessKeyId = "ASIASTUBASSUMEDROLE01";

        /// <summary>
        /// Instance used by the EC2 fixtures.
        /// </summary>
        public const string InstanceId = "i-0123456789abcdef0";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Default responses keyed by API action.
        /// </summary>
        /// <returns>Responses.</returns>
        public static Dictionary<string, AwsStubResponse> Defaults()
        {
            Dictionary<string, AwsStubResponse> d = new Dictionary<string, AwsStubResponse>(StringComparer.Ordinal);
            d["GetCallerIdentity"] = AwsStubResponse.Xml(GetCallerIdentity);
            d["AssumeRole"] = AwsStubResponse.Xml(AssumeRole());
            d["DescribeDBInstances"] = AwsStubResponse.Xml(DescribeDBInstances);
            d["DescribeDBClusters"] = AwsStubResponse.Xml(DescribeDBClusters);
            d["DescribeEvents"] = AwsStubResponse.Xml(DescribeEvents());
            d["DescribeLoadBalancers"] = AwsStubResponse.Xml(DescribeLoadBalancers);
            d["DescribeTargetGroups"] = AwsStubResponse.Xml(DescribeTargetGroups);
            d["DescribeTargetHealth"] = AwsStubResponse.Xml(DescribeTargetHealth);
            d["DescribeAutoScalingGroups"] = AwsStubResponse.Xml(DescribeAutoScalingGroups);
            d["ListServices"] = AwsStubResponse.Json(ListServices);
            d["DescribeServices"] = AwsStubResponse.Json(DescribeServices);
            d["UpdateService"] = AwsStubResponse.Json(UpdateService);
            d["DescribeInstances"] = AwsStubResponse.Xml(DescribeInstances);
            d["StartInstances"] = AwsStubResponse.Xml(StateChange("StartInstancesResponse", "instancesSet", 80, "stopped", 0, "pending"));
            d["StopInstances"] = AwsStubResponse.Xml(StateChange("StopInstancesResponse", "instancesSet", 16, "running", 64, "stopping"));
            d["TerminateInstances"] = AwsStubResponse.Xml(StateChange("TerminateInstancesResponse", "instancesSet", 16, "running", 32, "shutting-down"));
            d["RebootInstances"] = AwsStubResponse.Xml("<RebootInstancesResponse xmlns=\"http://ec2.amazonaws.com/doc/2016-11-15/\"><requestId>stub</requestId><return>true</return></RebootInstancesResponse>");
            return d;
        }

        #endregion

        #region Private-Members

        private const string GetCallerIdentity =
            "<GetCallerIdentityResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><GetCallerIdentityResult>"
            + "<Arn>arn:aws:iam::123456789012:user/hugo-stub</Arn><UserId>AIDASTUBUSER000000001</UserId><Account>123456789012</Account>"
            + "</GetCallerIdentityResult><ResponseMetadata><RequestId>01234567-89ab-cdef-0123-456789abcdef</RequestId></ResponseMetadata></GetCallerIdentityResponse>";

        private const string DescribeDBInstances =
            "<DescribeDBInstancesResponse xmlns=\"http://rds.amazonaws.com/doc/2014-10-31/\"><DescribeDBInstancesResult><DBInstances>"
            + "<DBInstance><DBInstanceIdentifier>orders-db</DBInstanceIdentifier><DBInstanceClass>db.r6g.large</DBInstanceClass><Engine>postgres</Engine>"
            + "<EngineVersion>16.4</EngineVersion><DBInstanceStatus>available</DBInstanceStatus><Endpoint><Address>orders-db.c1example.us-east-1.rds.amazonaws.com</Address><Port>5432</Port></Endpoint>"
            + "<AllocatedStorage>200</AllocatedStorage><AvailabilityZone>us-east-1a</AvailabilityZone><MultiAZ>true</MultiAZ></DBInstance>"
            + "<DBInstance><DBInstanceIdentifier>inventory-aurora-1</DBInstanceIdentifier><DBInstanceClass>db.r6g.xlarge</DBInstanceClass><Engine>aurora-mysql</Engine>"
            + "<EngineVersion>8.0.mysql_aurora.3.05.2</EngineVersion><DBInstanceStatus>modifying</DBInstanceStatus><Endpoint><Address>inventory-aurora-1.c1example.us-east-1.rds.amazonaws.com</Address><Port>3306</Port></Endpoint>"
            + "<AllocatedStorage>1</AllocatedStorage><AvailabilityZone>us-east-1b</AvailabilityZone><MultiAZ>false</MultiAZ><DBClusterIdentifier>inventory-aurora</DBClusterIdentifier></DBInstance>"
            + "</DBInstances><Marker>stub-rds-marker-2</Marker></DescribeDBInstancesResult><ResponseMetadata><RequestId>stub-rds-1</RequestId></ResponseMetadata></DescribeDBInstancesResponse>";

        private const string DescribeDBClusters =
            "<DescribeDBClustersResponse xmlns=\"http://rds.amazonaws.com/doc/2014-10-31/\"><DescribeDBClustersResult><DBClusters>"
            + "<DBCluster><DBClusterIdentifier>inventory-aurora</DBClusterIdentifier><Status>available</Status><Engine>aurora-mysql</Engine><EngineVersion>8.0.mysql_aurora.3.05.2</EngineVersion>"
            + "<Endpoint>inventory-aurora.cluster-c1example.us-east-1.rds.amazonaws.com</Endpoint><ReaderEndpoint>inventory-aurora.cluster-ro-c1example.us-east-1.rds.amazonaws.com</ReaderEndpoint><MultiAZ>true</MultiAZ>"
            + "<DBClusterMembers><DBClusterMember><DBInstanceIdentifier>inventory-aurora-1</DBInstanceIdentifier><IsClusterWriter>true</IsClusterWriter></DBClusterMember>"
            + "<DBClusterMember><DBInstanceIdentifier>inventory-aurora-2</DBInstanceIdentifier><IsClusterWriter>false</IsClusterWriter></DBClusterMember></DBClusterMembers></DBCluster>"
            + "</DBClusters></DescribeDBClustersResult><ResponseMetadata><RequestId>stub-rds-2</RequestId></ResponseMetadata></DescribeDBClustersResponse>";

        private const string DescribeLoadBalancers =
            "<DescribeLoadBalancersResponse xmlns=\"http://elasticloadbalancing.amazonaws.com/doc/2015-12-01/\"><DescribeLoadBalancersResult><LoadBalancers><member>"
            + "<LoadBalancerArn>arn:aws:elasticloadbalancing:us-east-1:123456789012:loadbalancer/app/web-alb/50dc6c495c0c9188</LoadBalancerArn><LoadBalancerName>web-alb</LoadBalancerName>"
            + "<Type>application</Type><Scheme>internet-facing</Scheme><State><Code>active</Code></State></member></LoadBalancers></DescribeLoadBalancersResult>"
            + "<ResponseMetadata><RequestId>stub-elb-1</RequestId></ResponseMetadata></DescribeLoadBalancersResponse>";

        private const string DescribeTargetGroups =
            "<DescribeTargetGroupsResponse xmlns=\"http://elasticloadbalancing.amazonaws.com/doc/2015-12-01/\"><DescribeTargetGroupsResult><TargetGroups><member>"
            + "<TargetGroupArn>arn:aws:elasticloadbalancing:us-east-1:123456789012:targetgroup/web-tg/73e2d6bc24d8a067</TargetGroupArn><TargetGroupName>web-tg</TargetGroupName>"
            + "<Protocol>HTTP</Protocol><Port>8080</Port><TargetType>instance</TargetType></member></TargetGroups></DescribeTargetGroupsResult>"
            + "<ResponseMetadata><RequestId>stub-elb-2</RequestId></ResponseMetadata></DescribeTargetGroupsResponse>";

        private const string DescribeTargetHealth =
            "<DescribeTargetHealthResponse xmlns=\"http://elasticloadbalancing.amazonaws.com/doc/2015-12-01/\"><DescribeTargetHealthResult><TargetHealthDescriptions>"
            + "<member><Target><Id>i-0123456789abcdef0</Id><Port>8080</Port><AvailabilityZone>us-east-1a</AvailabilityZone></Target><HealthCheckPort>8080</HealthCheckPort><TargetHealth><State>healthy</State></TargetHealth></member>"
            + "<member><Target><Id>i-0fedcba9876543210</Id><Port>8080</Port><AvailabilityZone>us-east-1b</AvailabilityZone></Target><HealthCheckPort>8080</HealthCheckPort>"
            + "<TargetHealth><State>unhealthy</State><Reason>Target.ResponseCodeMismatch</Reason><Description>Health checks failed with these codes: [502]</Description></TargetHealth></member>"
            + "</TargetHealthDescriptions></DescribeTargetHealthResult><ResponseMetadata><RequestId>stub-elb-3</RequestId></ResponseMetadata></DescribeTargetHealthResponse>";

        private const string DescribeAutoScalingGroups =
            "<DescribeAutoScalingGroupsResponse xmlns=\"http://autoscaling.amazonaws.com/doc/2011-01-01/\"><DescribeAutoScalingGroupsResult><AutoScalingGroups><member>"
            + "<AutoScalingGroupName>web-asg</AutoScalingGroupName><AutoScalingGroupARN>arn:aws:autoscaling:us-east-1:123456789012:autoScalingGroup:abc:autoScalingGroupName/web-asg</AutoScalingGroupARN>"
            + "<LaunchTemplate><LaunchTemplateId>lt-0abc</LaunchTemplateId><LaunchTemplateName>web-template</LaunchTemplateName><Version>$Latest</Version></LaunchTemplate>"
            + "<MinSize>2</MinSize><MaxSize>6</MaxSize><DesiredCapacity>3</DesiredCapacity><DefaultCooldown>300</DefaultCooldown>"
            + "<AvailabilityZones><member>us-east-1a</member><member>us-east-1b</member></AvailabilityZones><HealthCheckType>ELB</HealthCheckType><HealthCheckGracePeriod>300</HealthCheckGracePeriod>"
            + "<CreatedTime>2026-09-01T10:00:00.000Z</CreatedTime><Instances>"
            + "<member><InstanceId>i-0123456789abcdef0</InstanceId><AvailabilityZone>us-east-1a</AvailabilityZone><LifecycleState>InService</LifecycleState><HealthStatus>Healthy</HealthStatus><ProtectedFromScaleIn>false</ProtectedFromScaleIn></member>"
            + "<member><InstanceId>i-0fedcba9876543210</InstanceId><AvailabilityZone>us-east-1b</AvailabilityZone><LifecycleState>InService</LifecycleState><HealthStatus>Unhealthy</HealthStatus><ProtectedFromScaleIn>false</ProtectedFromScaleIn></member>"
            + "<member><InstanceId>i-0aaaabbbbccccdddd</InstanceId><AvailabilityZone>us-east-1a</AvailabilityZone><LifecycleState>Pending</LifecycleState><HealthStatus>Healthy</HealthStatus><ProtectedFromScaleIn>false</ProtectedFromScaleIn></member>"
            + "</Instances></member></AutoScalingGroups></DescribeAutoScalingGroupsResult><ResponseMetadata><RequestId>stub-asg-1</RequestId></ResponseMetadata></DescribeAutoScalingGroupsResponse>";

        private const string ListServices =
            "{\"serviceArns\":[\"arn:aws:ecs:us-east-1:123456789012:service/shop/checkout\",\"arn:aws:ecs:us-east-1:123456789012:service/shop/catalog\"],\"nextToken\":\"stub-ecs-next\"}";

        private const string DescribeServices =
            "{\"services\":[{\"serviceArn\":\"arn:aws:ecs:us-east-1:123456789012:service/shop/checkout\",\"serviceName\":\"checkout\",\"clusterArn\":\"arn:aws:ecs:us-east-1:123456789012:cluster/shop\","
            + "\"status\":\"ACTIVE\",\"desiredCount\":4,\"runningCount\":3,\"pendingCount\":1,\"launchType\":\"FARGATE\",\"taskDefinition\":\"arn:aws:ecs:us-east-1:123456789012:task-definition/checkout:42\","
            + "\"deployments\":[{\"id\":\"ecs-svc/1\",\"status\":\"PRIMARY\",\"taskDefinition\":\"arn:aws:ecs:us-east-1:123456789012:task-definition/checkout:42\",\"desiredCount\":4,\"pendingCount\":1,\"runningCount\":3,\"rolloutState\":\"IN_PROGRESS\",\"createdAt\":1792000000.0}],"
            + "\"events\":[{\"id\":\"e1\",\"createdAt\":1792000100.0,\"message\":\"(service checkout) has started 1 tasks: (task 0f1e2d).\"},"
            + "{\"id\":\"e2\",\"createdAt\":1792000200.0,\"message\":\"(service checkout) (task 9a8b7c) failed container health checks.\"}]},"
            + "{\"serviceArn\":\"arn:aws:ecs:us-east-1:123456789012:service/shop/catalog\",\"serviceName\":\"catalog\",\"status\":\"ACTIVE\",\"desiredCount\":2,\"runningCount\":2,\"pendingCount\":0,"
            + "\"launchType\":\"EC2\",\"taskDefinition\":\"arn:aws:ecs:us-east-1:123456789012:task-definition/catalog:7\",\"deployments\":[],\"events\":[]}],"
            + "\"failures\":[{\"arn\":\"arn:aws:ecs:us-east-1:123456789012:service/shop/ghost\",\"reason\":\"MISSING\"}]}";

        private const string UpdateService =
            "{\"service\":{\"serviceArn\":\"arn:aws:ecs:us-east-1:123456789012:service/shop/checkout\",\"serviceName\":\"checkout\",\"status\":\"ACTIVE\",\"desiredCount\":6,\"runningCount\":3,"
            + "\"pendingCount\":0,\"taskDefinition\":\"arn:aws:ecs:us-east-1:123456789012:task-definition/checkout:42\",\"deployments\":[{\"id\":\"ecs-svc/2\",\"status\":\"PRIMARY\"},{\"id\":\"ecs-svc/1\",\"status\":\"ACTIVE\"}]}}";

        private const string DescribeInstances =
            "<DescribeInstancesResponse xmlns=\"http://ec2.amazonaws.com/doc/2016-11-15/\"><requestId>stub-ec2-1</requestId><reservationSet><item><reservationId>r-0abc</reservationId><ownerId>123456789012</ownerId><instancesSet>"
            + "<item><instanceId>i-0123456789abcdef0</instanceId><imageId>ami-0abc</imageId><instanceState><code>16</code><name>running</name></instanceState><privateIpAddress>10.0.1.15</privateIpAddress>"
            + "<instanceType>t3.micro</instanceType><launchTime>2026-10-01T08:00:00.000Z</launchTime><placement><availabilityZone>us-east-1a</availabilityZone></placement>"
            + "<tagSet><item><key>Name</key><value>web-1</value></item></tagSet></item>"
            + "</instancesSet></item></reservationSet></DescribeInstancesResponse>";

        #endregion

        #region Private-Methods

        private static string AssumeRole()
        {
            string expires = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            return "<AssumeRoleResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><AssumeRoleResult><Credentials>"
                + "<AccessKeyId>" + AssumedAccessKeyId + "</AccessKeyId><SecretAccessKey>stub-assumed-secret</SecretAccessKey><SessionToken>stub-session-token</SessionToken>"
                + "<Expiration>" + expires + "</Expiration></Credentials><AssumedRoleUser><AssumedRoleId>AROASTUB:hugo</AssumedRoleId>"
                + "<Arn>arn:aws:sts::123456789012:assumed-role/HugoReadOnly/hugo</Arn></AssumedRoleUser></AssumeRoleResult>"
                + "<ResponseMetadata><RequestId>stub-sts-2</RequestId></ResponseMetadata></AssumeRoleResponse>";
        }

        private static string DescribeEvents()
        {
            string t1 = DateTime.UtcNow.AddMinutes(-30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            string t2 = DateTime.UtcNow.AddMinutes(-10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            return "<DescribeEventsResponse xmlns=\"http://rds.amazonaws.com/doc/2014-10-31/\"><DescribeEventsResult><Events>"
                + "<Event><SourceIdentifier>orders-db</SourceIdentifier><SourceType>db-instance</SourceType><Message>Multi-AZ instance failover started.</Message>"
                + "<EventCategories><EventCategory>failover</EventCategory></EventCategories><Date>" + t1 + "</Date></Event>"
                + "<Event><SourceIdentifier>orders-db</SourceIdentifier><SourceType>db-instance</SourceType><Message>Multi-AZ instance failover completed.</Message>"
                + "<EventCategories><EventCategory>failover</EventCategory></EventCategories><Date>" + t2 + "</Date></Event>"
                + "</Events></DescribeEventsResult><ResponseMetadata><RequestId>stub-rds-3</RequestId></ResponseMetadata></DescribeEventsResponse>";
        }

        private static string StateChange(string root, string set, int prevCode, string prevName, int curCode, string curName)
        {
            return "<" + root + " xmlns=\"http://ec2.amazonaws.com/doc/2016-11-15/\"><requestId>stub-ec2</requestId><" + set + "><item><instanceId>" + InstanceId + "</instanceId>"
                + "<currentState><code>" + curCode.ToString(CultureInfo.InvariantCulture) + "</code><name>" + curName + "</name></currentState>"
                + "<previousState><code>" + prevCode.ToString(CultureInfo.InvariantCulture) + "</code><name>" + prevName + "</name></previousState></item></" + set + "></" + root + ">";
        }

        #endregion
    }
}
