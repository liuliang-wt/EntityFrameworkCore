// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.NavigationExpansion;
using Microsoft.EntityFrameworkCore.Utilities;
using Remotion.Linq.Clauses.Expressions;

namespace Microsoft.EntityFrameworkCore.Query.Pipeline
{
    /// <summary>
    /// Rewrites comparisons of entities (as opposed to comparisons of their properties) into comparison of their keys.
    /// </summary>
    /// <remarks>
    /// For example, an expression such as cs.Where(c => c == something) would be rewritten to cs.Where(c => c.Id == something.Id).
    /// </remarks>
    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]  // TODO: Check
    public class EntityEqualityRewritingExpressionVisitor2 : ExpressionVisitor
    {
        protected IModel Model { get; }

        [CanBeNull]
        protected IEntityType MainEntityType { get; set; }

        [CanBeNull]
        protected INavigation LastNavigation { get; set; }

        // TODO: Use arrays or something, we're not going to have a large number here?
        [NotNull]
        protected Dictionary<ParameterExpression, IEntityType> ParameterBindings { get; private set; }

        protected Stack<(IEntityType MainEntityType, Dictionary<ParameterExpression, IEntityType> ParameterBindings)> Stack { get; }

        private static readonly MethodInfo _objectEqualsMethodInfo
            = typeof(object).GetRuntimeMethod(nameof(object.Equals), new[] { typeof(object), typeof(object) });

        public EntityEqualityRewritingExpressionVisitor2([NotNull] IModel model)
        {
            Model = model;
            ParameterBindings = new Dictionary<ParameterExpression, IEntityType>();
            Stack = new Stack<(IEntityType MainEntityType, Dictionary<ParameterExpression, IEntityType> ParameterBindings)>();
        }

        protected override Expression VisitConstant(ConstantExpression constantExpression)
        {
            if (constantExpression.IsEntityQueryable())
            {
                MainEntityType = Model.FindEntityType(((IQueryable)constantExpression.Value).ElementType);
            }

            return constantExpression;
        }

        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            var newExpression = (MemberExpression)base.VisitMember(memberExpression);

            LastNavigation = MainEntityType?.FindNavigation(newExpression.Member.Name);
            MainEntityType = LastNavigation?.GetTargetType();

            return newExpression;
        }

        protected override Expression VisitParameter(ParameterExpression parameterExpression)
        {
            MainEntityType = ParameterBindings.TryGetValue(parameterExpression, out var parameterEntityType)
                ? parameterEntityType
                : null;

            return parameterExpression;
        }

        protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            // Check if this is this Equals()
            if (methodCallExpression.Method.Name == nameof(object.Equals)
                && methodCallExpression.Object != null
                && methodCallExpression.Arguments.Count == 1)
            {
                MainEntityType = null;
                var left = methodCallExpression.Object;
                var right = methodCallExpression.Arguments[0];
                return RewriteEquality(true, ref left, ref right) is Expression rewritten
                    ? rewritten
                    : methodCallExpression.Update(left, new[] { right });
            }

            if (methodCallExpression.Method.Equals(_objectEqualsMethodInfo))
            {
                MainEntityType = null;
                var left = methodCallExpression.Arguments[0];
                var right = methodCallExpression.Arguments[1];
                return RewriteEquality(true, ref left, ref right) is Expression rewritten
                    ? rewritten
                    : methodCallExpression.Update(null, new[] { left, right });
            }

            // Navigation via EF.Property() or via an indexer property
            if (methodCallExpression.TryGetEFPropertyArguments(out _, out var propertyName)
                || methodCallExpression.TryGetEFIndexerArguments(out _, out propertyName))
            {
                LastNavigation = MainEntityType?.FindNavigation(propertyName);
                MainEntityType = LastNavigation?.GetTargetType();
                return base.VisitMethodCall(methodCallExpression);
            }

            var args = methodCallExpression.Arguments;

            if (methodCallExpression.Method.DeclaringType == typeof(Queryable)
                || methodCallExpression.Method.DeclaringType == typeof(EntityQueryableExtensions))
            {
                switch (methodCallExpression.Method.Name)
                {
                    case nameof(Queryable.Cast):
                    case nameof(Queryable.Concat):
                    case nameof(Queryable.DefaultIfEmpty):
                    case nameof(Queryable.Distinct):
                    case nameof(Queryable.ElementAtOrDefault):
                    case nameof(Queryable.Except):
                    case nameof(Queryable.FirstOrDefault):
                    case nameof(Queryable.Intersect):
                    case nameof(Queryable.LastOrDefault):
                    case nameof(Queryable.OfType):
                    case nameof(Queryable.OrderBy):
                    case nameof(Queryable.Reverse):
                    case nameof(Queryable.SingleOrDefault):
                    case nameof(Queryable.Skip):
                    case nameof(Queryable.SkipWhile):
                    case nameof(Queryable.Take):
                    case nameof(Queryable.TakeWhile):
                    case nameof(Queryable.ThenBy):
                    case nameof(Queryable.Union):
                    case nameof(Queryable.Where):
                        // Methods which flow the entity type through, some requiring lambda rewriting
                        return RewriteSimpleMethodIfNeeded(methodCallExpression);

                    case nameof(Queryable.All):
                    case nameof(Queryable.Any):
                    case nameof(Queryable.Average):
                    case nameof(Queryable.Contains):
                    case nameof(Queryable.Count):
                    case nameof(Queryable.LongCount):
                    case nameof(Queryable.Max):
                    case nameof(Queryable.Min):
                    case nameof(Queryable.Sum):
                    {
                        // Reducing methods: do not flow the entity type.
                        var newMethodCallExpression = RewriteSimpleMethodIfNeeded(methodCallExpression);
                        MainEntityType = null;
                        return newMethodCallExpression;
                    }

                    case nameof(Queryable.Select) when args.Count == 2:
                    case nameof(Queryable.SelectMany) when args.Count == 2:
                        // Projecting methods: flow the entity type from *within* the lambda.
                        // TODO: This does not handle anonymous types yet
                        return RewriteSimpleMethodIfNeeded(methodCallExpression, preserveMainEntityType: true);

                    case nameof(Queryable.SelectMany) when args.Count > 2:
                        // TODO: Need to rewrite manually - has more than one lambda + parameter
                        break;

                    case nameof(Queryable.GroupBy):
                    case nameof(Queryable.GroupJoin):
                    case nameof(Queryable.Join):
                    case nameof(EntityQueryableExtensions.LeftJoin):
                    {
                        // TODO: We can probably flow the entity type, but leaving for now
                        var newMethodCallExpression = RewriteSimpleMethodIfNeeded(methodCallExpression);
                        MainEntityType = null;
                        return newMethodCallExpression;
                    }
                }
            }

            // TODO: We need to identify FromSql{Raw,Interpolated} but those are in relational. For now we match by name, should
            // subclass visitor instead and move to the extension point below.
            if (methodCallExpression.Method.DeclaringType.Name == "RelationalEntityQueryableExtensions"
                && methodCallExpression.Method.Name == "FromSqlOnQueryable")
            {
                // FromSql{Raw,Interpolated} simply flow the entity type; no action needed.
                return methodCallExpression;
            }

            // If we're here, the this is an unknown method call.
            // TODO: Need an extension point that can be overridden by subclassing visitors to recognize additional methods and flow through the entity type.
            MainEntityType = null;
            return methodCallExpression;
        }

        // If the method accepts a single lambda with a single parameter, assumes the parameter corresponds to the first argument
        // and visits the lambda, performing the necessary rewriting.
        private Expression RewriteSimpleMethodIfNeeded(MethodCallExpression methodCallExpression, bool preserveMainEntityType = false)
        {
            var foundLambda = false;
            Expression[] newArguments = null;
            for (var i = 0; i < methodCallExpression.Arguments.Count; i++)
            {
                var arg = methodCallExpression.Arguments[i];
                Expression newArg;
                switch (arg)
                {
                    case UnaryExpression quote when quote.NodeType == ExpressionType.Quote:
                    {
                        var lambda = (LambdaExpression)quote.Operand;
                        if (foundLambda || lambda.Parameters.Count != 1)
                        {
                            throw new NotSupportedException("Cannot rewrite method with more than one lambda or lambda parameter: " +
                                                            methodCallExpression.Method.Name);
                        }

                        PushStackFrame(lambda.Parameters[0], MainEntityType);
                        newArg = Visit(quote);
                        PopStackFrame(preserveMainEntityType);

                        foundLambda = true;
                        break;
                    }

                    default:
                        newArg = Visit(arg);
                        break;
                }

                // Write the visited argument into a new arguments array, but only if any argument has already been modified
                if (newArg != arg)
                {
                    if (newArguments == null)
                    {
                        newArguments = new Expression[methodCallExpression.Arguments.Count];
                        methodCallExpression.Arguments.CopyTo(newArguments, 0);
                    }
                }

                if (newArguments != null)
                {
                    newArguments[i] = newArg;
                }
            }

            return methodCallExpression.Update(
                Visit(methodCallExpression.Object),
                (IEnumerable<Expression>)newArguments ?? methodCallExpression.Arguments);
        }

        private void PushStackFrame(ParameterExpression newParameterExpression, IEntityType newEntityType)
        {
            Stack.Push((MainEntityType, ParameterBindings));
            MainEntityType = null;
            ParameterBindings = new Dictionary<ParameterExpression, IEntityType>(ParameterBindings)
            {
                { newParameterExpression, newEntityType }
            };
        }

        private void PopStackFrame(bool preserveMainEntityType = false)
        {
            var frame = Stack.Pop();
            ParameterBindings = frame.ParameterBindings;
            if (!preserveMainEntityType)
            {
                MainEntityType = frame.MainEntityType;
            }
        }

        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            // TODO: This is a safety measure for now - not sure if any other binary expressions can occur with entity types directly
            // as their operands. But just in case we don't flow.
            MainEntityType = null;

            if (binaryExpression.NodeType == ExpressionType.Equal || binaryExpression.NodeType == ExpressionType.NotEqual)
            {
                var left = binaryExpression.Left;
                var right = binaryExpression.Right;
                return RewriteEquality(binaryExpression.NodeType == ExpressionType.Equal, ref left, ref right) is Expression rewritten
                    ? rewritten
                    : binaryExpression.Update(left, binaryExpression.Conversion, right);
            }

            return base.VisitBinary(binaryExpression);
        }

        /// <summary>
        /// Attempts to perform entity equality rewriting. If successful, returns the rewritten expression. Otherwise, returns null
        /// and <paramref name="left"/>  and <paramref name="right"/> contain the visited operands.
        /// </summary>
        protected virtual Expression RewriteEquality(bool isEqual, ref Expression left, ref Expression right)
        {
            // Visit children and get their respective entity types
            // TODO: Consider throwing if a child has no flowed entity type, but has a Type that corresponds to an entity type on the model.
            // TODO: This would indicate an issue in our flowing logic, and would help the user (and us) understand what's going on.
            left = Visit(left);
            var leftEntityType = MainEntityType;
            var leftLastNavigation = leftEntityType == null ? null :LastNavigation;
            MainEntityType = null;

            right = Visit(right);
            var rightEntityType = MainEntityType;
            var rightLastNavigation = rightEntityType == null ? null :LastNavigation;
            MainEntityType = null;

            // Handle null constants
            if (left.IsNullConstantExpression())
            {
                if (right.IsNullConstantExpression())
                {
                    return isEqual ? Expression.Constant(true) : Expression.Constant(false);
                }

                return rightEntityType == null
                    ? null
                    : RewriteNullEquality(isEqual, right, rightEntityType, rightLastNavigation);
            }

            if (right.IsNullConstantExpression())
            {
                return leftEntityType == null
                    ? null
                    : RewriteNullEquality(isEqual, left, leftEntityType, leftLastNavigation);
            }

            // No null constants, check the entity types on both sides

            if (leftEntityType != null && rightEntityType != null)
            {
                // TODO: Also compare primary keys, for the case of different owned/shared entity types with different primary key definition.
                if (leftEntityType.RootType() == rightEntityType.RootType())
                {
                    // TODO: Previous implementation always passed ExpressionType.Equal...?
                    var result = RewriteEntityEquality(
                        isEqual,
                        leftEntityType,
                        left, leftLastNavigation,
                        right, rightLastNavigation);

                    if (result != null)
                    {
                        return result;
                    }
                }
                else
                {
                    return Expression.Constant(false);
                }
            }

            return null;
        }

        private static Expression RewriteNullEquality(
            bool isEqual,
            [NotNull] Expression nonNullExpression,
            [NotNull] IEntityType entityType,
            [CanBeNull] INavigation lastNavigation)
        {
            if (lastNavigation?.IsCollection() == true)
            {
                // collection navigation is only null if its parent entity is null (null propagation thru navigation)
                // it is probable that user wanted to see if the collection is (not) empty
                // log warning suggesting to use Any() instead.
                // TODO: Bring back logging
                //_queryCompilationContext.Logger.PossibleUnintendedCollectionNavigationNullComparisonWarning(properties);

                // TODO: Note: previous implementation recursively called Visit() on unwrapped navigation expressions. We only need
                // only level of recursion on this method, since a navigation to a collection comes from a non-collection.
                return RewriteNullEquality(isEqual, UnwrapLastNavigation(nonNullExpression), lastNavigation.DeclaringEntityType, null);
            }

            // TODO: Need to reimplement?
            // if (IsInvalidSubQueryExpression(nonNullExpression))
            // {
            //     return null;
            // }

            var keyProperties = entityType.FindPrimaryKey().Properties;
            var nullCount = keyProperties.Count;

            // Skipping composite key with subquery since it requires to copy subquery
            // which would cause same subquery to be visited twice
            if (nullCount > 1 && nonNullExpression.RemoveConvert() is SubQueryExpression)
            {
                return null;
            }

            // TODO: bring back foreign key comparison optimization (#15826)

            var keyAccessExpression = CreateKeyAccessExpression(
                nonNullExpression,
                keyProperties,
                nullComparison: true);

            // TODO: any reason to to generate an anonymous object expression rather than a simple AndAlso expression tree?
            var nullConstantExpression
                = keyAccessExpression.Type == typeof(AnonymousObject)
                    ? Expression.New(
                        AnonymousObject.AnonymousObjectCtor,
                        Expression.NewArrayInit(
                            typeof(object),
                            Enumerable.Repeat(
                                Expression.Constant(null),
                                nullCount)))
                    : (Expression)Expression.Constant(null);

            return Expression.MakeBinary(isEqual ? ExpressionType.Equal : ExpressionType.NotEqual, keyAccessExpression, nullConstantExpression);
        }

        private static Expression RewriteEntityEquality(
            bool isEqual,
            [NotNull] IEntityType entityType,
            [NotNull] Expression left,
            [CanBeNull] INavigation leftNavigation,
            [NotNull] Expression right,
            [CanBeNull] INavigation rightNavigation)
        {
            // TODO: previous implementation only checked if the left side is a collection navigation
            // Collection navigations on both sides
            if (leftNavigation?.IsCollection() == true || rightNavigation?.IsCollection() == true)
            {
                if (leftNavigation?.Equals(rightNavigation) == true)
                {
                    // Log a warning that comparing 2 collections causes reference comparison
                    // TODO: Bring back error
                    //_queryCompilationContext.Logger
                    //    .PossibleUnintendedReferenceComparisonWarning(left, right);

                    // TODO: Note: previous implementation recursively called Visit() on unwrapped navigation expressions. We only need
                    // only level of recursion on this method, since a navigation to a collection comes from a non-collection.
                    return RewriteEntityEquality(
                        isEqual,
                        leftNavigation.DeclaringEntityType,
                        UnwrapLastNavigation(left),
                        null,
                        UnwrapLastNavigation(right),
                        null);
                }

                return Expression.Constant(!isEqual);
            }

            // if (IsInvalidSubQueryExpression(left)
            //     || IsInvalidSubQueryExpression(right))
            // {
            //     return null;
            // }

            var keyProperties = entityType.FindPrimaryKey().Properties;

            // Skipping composite key with subquery since it requires to copy subquery
            // which would cause same subquery to be visited twice
            return keyProperties.Count > 1
                   && (left.RemoveConvert() is SubQueryExpression
                       || right.RemoveConvert() is SubQueryExpression)
                ? null
                : Expression.MakeBinary(
                    isEqual ? ExpressionType.Equal : ExpressionType.NotEqual,
                    CreateKeyAccessExpression(left, keyProperties, nullComparison: false),
                    CreateKeyAccessExpression(right, keyProperties, nullComparison: false));
        }

        private static Expression CreateKeyAccessExpression(
            Expression target,
            IReadOnlyList<IProperty> properties,
            bool nullComparison)
        {
            // If comparing with null then we need only first PK property
            return properties.Count == 1 || nullComparison
                ? target.CreateEFPropertyExpression(properties[0]) // TODO: Why via EF.Property()?
                : target.CreateKeyAccessExpression(properties);
        }

        private static Expression UnwrapLastNavigation(Expression expression)
            => (expression as MemberExpression)?.Expression
               ?? (expression is MethodCallExpression methodCallExpression
                   && methodCallExpression.Method.IsEFPropertyMethod()
                   ? methodCallExpression.Arguments[0]
                   : null);
    }
}
