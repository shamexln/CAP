﻿/*
 * Licensed to the OpenSkywalking under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using SkyWalking.Context;
using SkyWalking.Context.Tag;
using SkyWalking.Context.Trace;
using SkyWalking.NetworkProtocol.Trace;

namespace SkyWalking.Diagnostics.EntityFrameworkCore
{
    public class EntityFrameworkCoreDiagnosticProcessor : ITracingDiagnosticProcessor
    {
        private Func<CommandEventData, string> _operationNameResolver;

        public string ListenerName => DbLoggerCategory.Name;

        /// <summary>
        /// A delegate that returns the OpenTracing "operation name" for the given command.
        /// </summary>
        public Func<CommandEventData, string> OperationNameResolver
        {
            get
            {
                return _operationNameResolver ??
                       (_operationNameResolver = (data) => "DB " + data.ExecuteMethod.ToString());
            }
            set => _operationNameResolver = value ?? throw new ArgumentNullException(nameof(OperationNameResolver));
        }

        [DiagnosticName("Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting")]
        public void CommandExecuting([Object]CommandEventData eventData)
        {
            var operationName = OperationNameResolver(eventData);
            var span = ContextManager.CreateLocalSpan(operationName);
            span.SetComponent(ComponentsDefine.EntityFrameworkCore);
            span.SetLayer(SpanLayer.DB);
            Tags.DbInstance.Set(span, eventData.Command.Connection.Database);
            Tags.DbStatement.Set(span, eventData.Command.CommandText);
            Tags.DbBindVariables.Set(span, BuildParameterVariables(eventData.Command.Parameters));
        }

        [DiagnosticName("Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted")]
        public void CommandExecuted()
        {
            ContextManager.StopSpan();
        }

        [DiagnosticName("Microsoft.EntityFrameworkCore.Database.Command.CommandError")]
        public void CommandError(CommandErrorEventData eventData)
        {
            var span = ContextManager.ActiveSpan;
            if (span == null)
            {
                return;
            }

            if (eventData != null)
            {
                span.Log(eventData.Exception);
            }
            span.ErrorOccurred();
            ContextManager.StopSpan(span);
        }

        private string BuildParameterVariables(DbParameterCollection dbParameters)
        {
            if (dbParameters == null)
            {
                return string.Empty;
            }

            return dbParameters.FormatParameters(false);
        }
    }
}