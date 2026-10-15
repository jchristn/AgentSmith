#!/bin/bash
# Seeds the Hugo AWS testbed. Runs inside the LocalStack container from /etc/localstack/init/ready.d once every
# service is up. Idempotent enough for one run per container; `down -v` resets everything.
set -euo pipefail

export AWS_DEFAULT_REGION=us-east-1
AMI=ami-df5de72bdb3b
NOW=$(date -u +%s)

echo "hugo-testbed: EC2"
awslocal ec2 run-instances --image-id "$AMI" --count 2 --instance-type t3.micro \
  --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=hugo-web},{Key=env,Value=testbed}]' >/dev/null
awslocal ec2 run-instances --image-id "$AMI" --count 1 --instance-type t3.small \
  --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=hugo-worker},{Key=env,Value=testbed}]' >/dev/null

echo "hugo-testbed: CloudWatch"
for i in $(seq 1 12); do
  TS=$(date -u -d "@$((NOW - i * 300))" +%Y-%m-%dT%H:%M:%SZ)
  awslocal cloudwatch put-metric-data --namespace Hugo/Testbed --metric-data \
    "MetricName=RequestCount,Dimensions=[{Name=Service,Value=checkout}],Value=$((100 + i)),Timestamp=$TS" \
    "MetricName=Latency,Dimensions=[{Name=Service,Value=checkout}],Value=$((200 + i * 30)),Unit=Milliseconds,Timestamp=$TS"
done
awslocal cloudwatch put-metric-alarm --alarm-name hugo-testbed-high-latency --namespace Hugo/Testbed --metric-name Latency \
  --dimensions Name=Service,Value=checkout --statistic Average --period 60 --evaluation-periods 1 --threshold 500 \
  --comparison-operator GreaterThanThreshold --alarm-description "Checkout latency above 500 ms"
awslocal cloudwatch set-alarm-state --alarm-name hugo-testbed-high-latency --state-value ALARM \
  --state-reason "Testbed fixture: average latency 560 ms above the 500 ms threshold"
awslocal cloudwatch put-metric-alarm --alarm-name hugo-testbed-queue-depth --namespace AWS/SQS \
  --metric-name ApproximateNumberOfMessagesVisible --dimensions Name=QueueName,Value=hugo-testbed-orders --statistic Maximum \
  --period 300 --evaluation-periods 2 --threshold 1000 --comparison-operator GreaterThanOrEqualToThreshold
awslocal cloudwatch set-alarm-state --alarm-name hugo-testbed-queue-depth --state-value OK --state-reason "Testbed fixture"

echo "hugo-testbed: Logs"
awslocal logs create-log-group --log-group-name /hugo/testbed/app
for stream in web-1 web-2; do
  awslocal logs create-log-stream --log-group-name /hugo/testbed/app --log-stream-name "$stream"
  T=$(( (NOW - 600) * 1000 ))
  awslocal logs put-log-events --log-group-name /hugo/testbed/app --log-stream-name "$stream" --log-events \
    "timestamp=$T,message=INFO $stream started" \
    "timestamp=$((T + 1000)),message=WARN $stream slow response 870ms path=/checkout" \
    "timestamp=$((T + 2000)),message=ERROR $stream payment gateway timeout order=1001" \
    "timestamp=$((T + 3000)),message=INFO $stream healthy" >/dev/null
done

echo "hugo-testbed: SQS"
DLQ_URL=$(awslocal sqs create-queue --queue-name hugo-testbed-orders-dlq --query QueueUrl --output text)
DLQ_ARN=$(awslocal sqs get-queue-attributes --queue-url "$DLQ_URL" --attribute-names QueueArn --query Attributes.QueueArn --output text)
cat > /tmp/hugo-queue-attributes.json <<EOF
{"VisibilityTimeout":"45","RedrivePolicy":"{\"deadLetterTargetArn\":\"$DLQ_ARN\",\"maxReceiveCount\":\"5\"}"}
EOF
Q_URL=$(awslocal sqs create-queue --queue-name hugo-testbed-orders --attributes file:///tmp/hugo-queue-attributes.json --query QueueUrl --output text)
for n in 1 2 3; do awslocal sqs send-message --queue-url "$Q_URL" --message-body "{\"order\":$n}" >/dev/null; done
Q_ARN=$(awslocal sqs get-queue-attributes --queue-url "$Q_URL" --attribute-names QueueArn --query Attributes.QueueArn --output text)

echo "hugo-testbed: SNS"
TOPIC_ARN=$(awslocal sns create-topic --name hugo-testbed-alerts --attributes DisplayName=HugoAlerts --query TopicArn --output text)
awslocal sns subscribe --topic-arn "$TOPIC_ARN" --protocol sqs --notification-endpoint "$Q_ARN" >/dev/null

echo "hugo-testbed: Route 53"
ZONE_ID=$(awslocal route53 create-hosted-zone --name testbed.hugo.internal --caller-reference "hugo-testbed-$NOW" --query HostedZone.Id --output text)
cat > /tmp/hugo-records.json <<'EOF'
{"Changes":[
 {"Action":"CREATE","ResourceRecordSet":{"Name":"www.testbed.hugo.internal","Type":"A","TTL":300,"ResourceRecords":[{"Value":"10.0.0.10"},{"Value":"10.0.0.11"}]}},
 {"Action":"CREATE","ResourceRecordSet":{"Name":"api.testbed.hugo.internal","Type":"CNAME","TTL":60,"ResourceRecords":[{"Value":"www.testbed.hugo.internal"}]}},
 {"Action":"CREATE","ResourceRecordSet":{"Name":"testbed.hugo.internal","Type":"TXT","TTL":300,"ResourceRecords":[{"Value":"\"hugo-testbed\""}]}}
]}
EOF
awslocal route53 change-resource-record-sets --hosted-zone-id "$ZONE_ID" --change-batch file:///tmp/hugo-records.json >/dev/null

echo "hugo-testbed: Lambda"
mkdir -p /tmp/hugo-echo
cat > /tmp/hugo-echo/handler.py <<'EOF'
import json


def handler(event, context):
    print("hugo testbed invoked with " + json.dumps(event))
    return {"ok": True, "echo": event}
EOF
python3 -c "import zipfile; z = zipfile.ZipFile('/tmp/hugo-echo.zip', 'w'); z.write('/tmp/hugo-echo/handler.py', 'handler.py'); z.close()"
awslocal lambda create-function --function-name hugo-testbed-echo --runtime python3.12 --handler handler.handler \
  --zip-file fileb:///tmp/hugo-echo.zip --role arn:aws:iam::000000000000:role/hugo-testbed-lambda --timeout 30 --memory-size 128 >/dev/null
awslocal lambda wait function-active-v2 --function-name hugo-testbed-echo
# Warm the runtime so the first test invocation does not pay for the image pull.
awslocal lambda invoke --function-name hugo-testbed-echo --payload '{"warmup":true}' /tmp/hugo-echo-out.json >/dev/null || echo "hugo-testbed: lambda warm-up failed (is the Docker socket mounted?)"

echo "hugo-testbed: seeded"
