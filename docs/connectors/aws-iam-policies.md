# IAM policies for the AWS connector

Hugo's classifier and approvals are the second line of defense. The first is the IAM policy behind the credentials you give the `aws` resource: if a binding should never stop an instance, the credentials should not be able to. This page lists exactly which IAM actions each tool calls and gives two policies to start from:

- **Read-only**, for resources bound as ReadOnly. Every Read tool works and nothing can change.
- **Operator**, for resources bound as ReadWriteWithApproval. It adds the handful of actions the Write and Destructive tools need, scoped by tag.

Both are starting points. Narrow `Resource` to specific ARNs where you can, and drop statements for services the resource does not need (and list only those services in the resource's `allowedServices` setting, so the agent never sees the other tools).

## Actions per tool

| Tool | Effect | IAM actions |
|---|---|---|
| `aws_caller_identity` | Read | `sts:GetCallerIdentity` (allowed for every principal; no statement needed) |
| `aws_ec2_describe_instances` | Read | `ec2:DescribeInstances` |
| `aws_autoscaling_describe_groups` | Read | `autoscaling:DescribeAutoScalingGroups` |
| `aws_ec2_instance_action` | start: Write; stop, reboot: Destructive; terminate: Destructive, always approved | `ec2:StartInstances`, `ec2:StopInstances`, `ec2:RebootInstances`, `ec2:TerminateInstances` |
| `aws_cloudwatch_get_metric_data` | Read | `cloudwatch:GetMetricData` |
| `aws_cloudwatch_list_alarms` | Read | `cloudwatch:DescribeAlarms` |
| `aws_logs_insights_query` | Read | `logs:StartQuery`, `logs:GetQueryResults` |
| `aws_logs_tail` | Read | `logs:FilterLogEvents` |
| `aws_ecs_describe_services` | Read | `ecs:ListServices`, `ecs:DescribeServices` |
| `aws_ecs_update_service` | Write (Destructive when scaling to 0) | `ecs:UpdateService` |
| `aws_rds_describe` | Read | `rds:DescribeDBInstances`, `rds:DescribeDBClusters`, `rds:DescribeEvents` |
| `aws_lambda_list_functions` | Read | `lambda:ListFunctions` |
| `aws_lambda_invoke` | Write | `lambda:InvokeFunction` |
| `aws_elb_target_health` | Read | `elasticloadbalancing:DescribeTargetHealth`, `elasticloadbalancing:DescribeTargetGroups`, `elasticloadbalancing:DescribeLoadBalancers` |
| `aws_route53_list_records` | Read | `route53:ListResourceRecordSets`, `route53:ListHostedZones`, `route53:ListHostedZonesByName` |
| `aws_sqs_queue_attributes` | Read | `sqs:GetQueueAttributes`, `sqs:GetQueueUrl`, `sqs:ListQueues` |
| `aws_sns_list_topics` | Read | `sns:ListTopics`, `sns:GetTopicAttributes` |

Logs Insights queries read every log group you name, so `logs:StartQuery` should be limited to the log groups the agent may search. KMS-encrypted log groups also need `kms:Decrypt` on the key.

## Read-only policy

Attach this to the IAM user or role whose credentials a ReadOnly resource uses.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "HugoDescribe",
      "Effect": "Allow",
      "Action": [
        "ec2:DescribeInstances",
        "autoscaling:DescribeAutoScalingGroups",
        "cloudwatch:GetMetricData",
        "cloudwatch:DescribeAlarms",
        "ecs:ListServices",
        "ecs:DescribeServices",
        "rds:DescribeDBInstances",
        "rds:DescribeDBClusters",
        "rds:DescribeEvents",
        "lambda:ListFunctions",
        "elasticloadbalancing:DescribeTargetHealth",
        "elasticloadbalancing:DescribeTargetGroups",
        "elasticloadbalancing:DescribeLoadBalancers",
        "route53:ListHostedZones",
        "route53:ListHostedZonesByName",
        "route53:ListResourceRecordSets",
        "sqs:ListQueues",
        "sqs:GetQueueUrl",
        "sqs:GetQueueAttributes",
        "sns:ListTopics",
        "sns:GetTopicAttributes"
      ],
      "Resource": "*"
    },
    {
      "Sid": "HugoLogs",
      "Effect": "Allow",
      "Action": [
        "logs:FilterLogEvents",
        "logs:StartQuery",
        "logs:GetQueryResults"
      ],
      "Resource": [
        "arn:aws:logs:us-east-1:123456789012:log-group:/aws/lambda/*",
        "arn:aws:logs:us-east-1:123456789012:log-group:/ecs/*",
        "arn:aws:logs:us-east-1:123456789012:log-group:/app/*"
      ]
    },
    {
      "Sid": "HugoLogsQueryResults",
      "Effect": "Allow",
      "Action": "logs:GetQueryResults",
      "Resource": "*"
    }
  ]
}
```

Replace the account ID, region, and log group patterns with yours. `logs:GetQueryResults` is not resource-scoped by AWS, which is why it has its own statement.

## Operator policy

Use this for a ReadWriteWithApproval resource. It includes everything in the read-only policy plus the actions behind the Write and Destructive tools, limited to resources tagged `hugo-managed = true`. Hugo still classifies every call and holds Write and Destructive calls for approval; the tag condition makes sure that even an approved call cannot touch anything you did not opt in.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "HugoReadOnly",
      "Effect": "Allow",
      "Action": [
        "ec2:DescribeInstances",
        "autoscaling:DescribeAutoScalingGroups",
        "cloudwatch:GetMetricData",
        "cloudwatch:DescribeAlarms",
        "ecs:ListServices",
        "ecs:DescribeServices",
        "rds:DescribeDBInstances",
        "rds:DescribeDBClusters",
        "rds:DescribeEvents",
        "lambda:ListFunctions",
        "elasticloadbalancing:DescribeTargetHealth",
        "elasticloadbalancing:DescribeTargetGroups",
        "elasticloadbalancing:DescribeLoadBalancers",
        "route53:ListHostedZones",
        "route53:ListHostedZonesByName",
        "route53:ListResourceRecordSets",
        "sqs:ListQueues",
        "sqs:GetQueueUrl",
        "sqs:GetQueueAttributes",
        "sns:ListTopics",
        "sns:GetTopicAttributes",
        "logs:FilterLogEvents",
        "logs:StartQuery",
        "logs:GetQueryResults"
      ],
      "Resource": "*"
    },
    {
      "Sid": "HugoInstanceLifecycle",
      "Effect": "Allow",
      "Action": [
        "ec2:StartInstances",
        "ec2:StopInstances",
        "ec2:RebootInstances"
      ],
      "Resource": "arn:aws:ec2:us-east-1:123456789012:instance/*",
      "Condition": {
        "StringEquals": { "aws:ResourceTag/hugo-managed": "true" }
      }
    },
    {
      "Sid": "HugoEcsDeploy",
      "Effect": "Allow",
      "Action": "ecs:UpdateService",
      "Resource": "arn:aws:ecs:us-east-1:123456789012:service/*/*",
      "Condition": {
        "StringEquals": { "aws:ResourceTag/hugo-managed": "true" }
      }
    },
    {
      "Sid": "HugoLambdaInvoke",
      "Effect": "Allow",
      "Action": "lambda:InvokeFunction",
      "Resource": "arn:aws:lambda:us-east-1:123456789012:function:ops-*"
    }
  ]
}
```

Notes on the operator policy:

- **Terminate is left out on purpose.** `aws_ec2_instance_action` with `terminate` always waits for a human in Hugo, but most teams still prefer that terminations go through their own tooling. To allow it, add `ec2:TerminateInstances` to `HugoInstanceLifecycle`, ideally in a separate statement with a stricter tag such as `hugo-may-terminate = true`. Enabling termination protection (`DisableApiTermination`) on important instances blocks it regardless.
- **`ecs:UpdateService`** lets the agent change the desired count and force a new deployment. The tool cannot change task definitions, networking, or load balancers, but IAM cannot express that limit, so tag only services you are happy to scale.
- **`lambda:InvokeFunction`** runs code with that function's own role. Restrict it to functions built for operators (here, names starting with `ops-`), never to functions that move money or delete data.
- Stopping instances that belong to an Auto Scaling group makes the group replace them. Hugo does not prevent that; exclude those instances by tag if it matters.

## Cross-account access with an external ID

To let Hugo work in another account without long-lived keys there, create a role in the target account, attach one of the policies above to it, and trust the principal whose keys (or instance role) Hugo uses. Require an external ID so the role cannot be assumed by anyone who merely learns its ARN.

Trust policy on the target role:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": { "AWS": "arn:aws:iam::111111111111:user/hugo-broker" },
      "Action": "sts:AssumeRole",
      "Condition": {
        "StringEquals": { "sts:ExternalId": "hugo-acme-prod-7f3c" }
      }
    }
  ]
}
```

Policy on the Hugo principal (`hugo-broker` in account 111111111111):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": "arn:aws:iam::222222222222:role/HugoReadOnly"
    }
  ]
}
```

Then set the resource's `assumeRoleArn` to `arn:aws:iam::222222222222:role/HugoReadOnly`, `externalId` to `hugo-acme-prod-7f3c`, and optionally `sessionName` so CloudTrail shows which Hugo resource made each call. Hugo refreshes the role credentials before they expire (`assumeRoleDurationSeconds`, default one hour; the role's own maximum session duration also applies).

## Instance and task roles

When Hugo runs on EC2 or ECS, turn on `useInstanceRole` instead of storing keys. The server's instance profile or task role then supplies the base credentials, and you attach the policies above to that role (or use it only to assume per-account roles as described above). Keep in mind that every resource with `useInstanceRole` shares the same base identity, so per-resource separation comes from `assumeRoleArn`, not from the instance role itself.

## Checking a policy

After binding the resource, run `aws_caller_identity` to confirm the account and principal, then a Read tool for each service you allowed. A missing permission comes back as `permission_denied` naming the IAM action, so you can add exactly what is needed. The IAM policy simulator (`aws iam simulate-principal-policy`) is a quick way to confirm that the operator policy denies an action on an untagged instance.
