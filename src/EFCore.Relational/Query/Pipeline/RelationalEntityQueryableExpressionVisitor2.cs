// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Pipeline;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.Relational.Query.Pipeline
{
    public class RelationalEntityQueryableExpressionVisitor2 : EntityQueryableExpressionVisitor2
    {
        private IModel _model;
        private readonly ISqlExpressionFactory _sqlExpressionFactory;

        public RelationalEntityQueryableExpressionVisitor2(IModel model, ISqlExpressionFactory sqlExpressionFactory)
        {
            _model = model;
            _sqlExpressionFactory = sqlExpressionFactory;
        }

        protected override ShapedQueryExpression CreateShapedQueryExpression(Type elementType)
        {
            var entityType = _model.FindEntityType(elementType);
            var queryExpression = _sqlExpressionFactory.Select(entityType);

            return new RelationalShapedQueryExpression(
                queryExpression,
                new EntityShaperExpression(
                entityType,
                new ProjectionBindingExpression(
                    queryExpression,
                    new ProjectionMember(),
                    typeof(ValueBuffer)),
                false));
        }
    }
}
