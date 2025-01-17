﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace aggregator
{
    /// <summary>
    /// Contains Aggregator specific code with no reference to Rule triggering
    /// </summary>
    internal class RuleWrapper
    {
        private readonly AggregatorConfiguration configuration;
        private readonly IAggregatorLogger logger;
        private readonly string ruleName;
        private readonly string functionDirectory;

        public RuleWrapper(AggregatorConfiguration configuration, IAggregatorLogger logger, string ruleName, string functionDirectory)
        {
            this.configuration = configuration;
            this.logger = logger;
            this.ruleName = ruleName;
            this.functionDirectory = functionDirectory;
        }

        internal async Task<string> ExecuteAsync(dynamic data, CancellationToken cancellationToken)
        {
            string collectionUrl = data.resourceContainers.collection.baseUrl;
            string eventType = data.eventType;
            int workItemId = (eventType != "workitem.updated")
                ? data.resource.id
                : data.resource.workItemId;
            Guid teamProjectId = data.resourceContainers.project.id;
            string teamProjectName = (eventType != "workitem.updated")
                ? data.resource.fields["System.TeamProject"]
                : data.resource.revision.fields["System.TeamProject"];

            logger.WriteVerbose($"Connecting to Azure DevOps using {configuration.DevOpsTokenType}...");
            var clientCredentials = default(VssCredentials);
            if (configuration.DevOpsTokenType == DevOpsTokenType.PAT)
            {
                clientCredentials = new VssBasicCredential(configuration.DevOpsTokenType.ToString(), configuration.DevOpsToken);
            }
            else
            {
                logger.WriteError($"Azure DevOps Token type {configuration.DevOpsTokenType} not supported!");
                throw new ArgumentOutOfRangeException(nameof(configuration.DevOpsTokenType));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // TODO improve from https://github.com/Microsoft/vsts-work-item-migrator
            using (var devops = new VssConnection(new Uri(collectionUrl), clientCredentials))
            {
                await devops.ConnectAsync(cancellationToken);
                logger.WriteInfo($"Connected to Azure DevOps");
                using (var witClient = devops.GetClient<WorkItemTrackingHttpClient>())
                {
                    string ruleFilePath = Path.Combine(functionDirectory, $"{ruleName}.rule");
                    if (!File.Exists(ruleFilePath))
                    {
                        logger.WriteError($"Rule code not found at {ruleFilePath}");
                        return "Rule file not found!";
                    }

                    logger.WriteVerbose($"Rule code found at {ruleFilePath}");
                    string[] ruleCode;
                    using (var fileStream = File.OpenRead(ruleFilePath))
                    {
                        var reader = new StreamReader(fileStream);
                        ruleCode = await ReadAllLinesAsync(reader);
                    }

                    var engine = new Engine.RuleEngine(logger, ruleCode, configuration.SaveMode, configuration.DryRun);

                    return await engine.ExecuteAsync(teamProjectId, teamProjectName, workItemId, witClient, cancellationToken);
                }
            }
        }

        private static async Task<string[]> ReadAllLinesAsync(TextReader streamReader)
        {
            var lines = new List<string>();
            string line;
            while ((line = await streamReader.ReadLineAsync()) != null)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }
    }
}
