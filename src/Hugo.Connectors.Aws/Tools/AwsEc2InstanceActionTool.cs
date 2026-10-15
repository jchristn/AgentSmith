namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.EC2.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_ec2_instance_action</c> (classified per call): start (Write), stop and reboot (Destructive), and terminate
    /// (Destructive, always requires approval even when the binding opts out of destructive approvals). An unknown action
    /// is Destructive (fail closed). Thread-safe.
    /// </summary>
    public class AwsEc2InstanceActionTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_ec2_instance_action";

        /// <summary>
        /// Most instances one call may act on. Default 20, minimum 1, maximum 1000.
        /// </summary>
        public int MaxInstancesPerCall
        {
            get
            {
                return _MaxInstancesPerCall;
            }
            set
            {
                _MaxInstancesPerCall = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxInstancesPerCall = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsEc2InstanceActionTool() : base(BuildDescriptor(), AwsServiceNames.Ec2, "ec2:StartInstances", "ec2:StopInstances", "ec2:RebootInstances", "ec2:TerminateInstances")
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// IAM operation for an action, or null when the action is unknown.
        /// </summary>
        /// <param name="action">Action: start, stop, reboot, or terminate.</param>
        /// <returns>Operation such as <c>ec2:StopInstances</c>, or null.</returns>
        public static string? OperationFor(string? action)
        {
            switch ((action ?? String.Empty).Trim().ToLowerInvariant())
            {
                case "start":
                    return "ec2:StartInstances";
                case "stop":
                    return "ec2:StopInstances";
                case "reboot":
                    return "ec2:RebootInstances";
                case "terminate":
                    return "ec2:TerminateInstances";
                default:
                    return null;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCore(ConnectorInvocationContext context, ToolArguments arguments, AwsOptions options)
        {
            string action = (arguments.GetString("action") ?? String.Empty).Trim().ToLowerInvariant();
            List<string> ids;
            try
            {
                ids = AwsArguments.InstanceIds(arguments.GetStringList("instanceIds"));
            }
            catch (ConnectorException e)
            {
                return EffectClassification.Reject(e.Message);
            }
            if (ids.Count == 0) return EffectClassification.Reject("instanceIds is required; name every instance explicitly");
            if (ids.Count > _MaxInstancesPerCall) return EffectClassification.Reject("at most " + _MaxInstancesPerCall + " instances per call");

            string? operation = OperationFor(action);
            if (operation == null)
            {
                return new EffectClassification(EffectEnum.Destructive, "unknown EC2 instance action '" + action + "' (fail closed)", "Unknown EC2 action '" + action + "' on " + String.Join(", ", ids) + " in " + Where(context));
            }

            EffectEnum effect = AwsOperationClassifier.Classify(operation);
            bool force = arguments.GetBool("force", false);
            string verb = action == "start" ? "Start" : action == "stop" ? (force ? "Force-stop" : "Stop") : action == "reboot" ? "Reboot" : "Terminate";
            string summary = verb + " EC2 instance" + (ids.Count == 1 ? " " : "s ") + String.Join(", ", ids) + " in " + Where(context);
            EffectClassification c = new EffectClassification(effect, operation + " is " + effect, summary);
            if (action == "terminate")
            {
                c.AlwaysRequireApproval = true;
                c.Reason = operation + " permanently deletes the instances and their instance-store and delete-on-termination volumes; always requires approval";
            }
            return c;
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            string action = (arguments.GetString("action") ?? String.Empty).Trim().ToLowerInvariant();
            if (OperationFor(action) == null) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "action must be start, stop, reboot, or terminate.");
            List<string> ids = AwsArguments.InstanceIds(arguments.GetStringList("instanceIds"));
            if (ids.Count == 0) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "instanceIds is required.");
            if (ids.Count > _MaxInstancesPerCall) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "At most " + _MaxInstancesPerCall + " instances per call.");

            AwsTable table = new AwsTable("EC2 " + action + " requested", "instanceId", "previousState", "currentState");
            List<InstanceStateChange> changes = new List<InstanceStateChange>();
            switch (action)
            {
                case "start":
                    StartInstancesResponse started = await session.Ec2().StartInstancesAsync(new StartInstancesRequest { InstanceIds = ids }, token).ConfigureAwait(false);
                    changes = started.StartingInstances ?? changes;
                    break;
                case "stop":
                    StopInstancesRequest stop = new StopInstancesRequest { InstanceIds = ids };
                    if (arguments.GetBool("force", false)) stop.Force = true;
                    StopInstancesResponse stopped = await session.Ec2().StopInstancesAsync(stop, token).ConfigureAwait(false);
                    changes = stopped.StoppingInstances ?? changes;
                    break;
                case "reboot":
                    await session.Ec2().RebootInstancesAsync(new RebootInstancesRequest { InstanceIds = ids }, token).ConfigureAwait(false);
                    foreach (string id in ids) table.Add(id, "running", "rebooting");
                    break;
                default:
                    TerminateInstancesResponse terminated = await session.Ec2().TerminateInstancesAsync(new TerminateInstancesRequest { InstanceIds = ids }, token).ConfigureAwait(false);
                    changes = terminated.TerminatingInstances ?? changes;
                    break;
            }

            foreach (InstanceStateChange change in changes)
                table.Add(change.InstanceId, change.PreviousState?.Name?.Value, change.CurrentState?.Name?.Value);
            table.Details["action"] = action;
            table.Details["instanceIds"] = String.Join(",", ids);
            table.Notes.Add("State changes are asynchronous; confirm with aws_ec2_describe_instances.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "EC2 instance action";
            d.Description =
                "Start, stop, reboot, or terminate EC2 instances named by ID. start is Write; stop and reboot are Destructive "
                + "and usually need approval; terminate is Destructive, irreversible, and always needs human approval. State "
                + "the instance IDs and the reason before calling. Check the current state with aws_ec2_describe_instances first.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"action\":{\"type\":\"string\",\"enum\":[\"start\",\"stop\",\"reboot\",\"terminate\"],\"description\":\"What to do.\"},"
                + "\"instanceIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":20,\"description\":\"Instance IDs (i-...).\"},"
                + "\"force\":{\"type\":\"boolean\",\"description\":\"stop only: force the stop without a graceful shutdown. Default false.\"}"
                + "},\"required\":[\"action\",\"instanceIds\"],\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Destructive;
            d.ClassifiedPerCall = true;
            d.OutputDescription = "Previous and current state of each instance.";
            return d;
        }

        #endregion
    }
}
