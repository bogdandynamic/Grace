﻿using System;
using System.Reflection;
using Grace.Data.Immutable;

namespace Grace.DependencyInjection.Impl.Expressions
{
    /// <summary>
    /// interface for creating wrappers around exports
    /// </summary>
    public interface IWrapperExpressionCreator
    {
        /// <summary>
        /// Get an activation expression for a request
        /// </summary>
        /// <param name="scope">scope for request</param>
        /// <param name="request">request</param>
        /// <returns></returns>
        IActivationExpressionResult GetActivationExpression(IInjectionScope scope, IActivationExpressionRequest request);

        /// <summary>
        /// Get wrappers for a request
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="type"></param>
        /// <param name="request"></param>
        /// <param name="wrappedType"></param>
        /// <returns></returns>
        ImmutableLinkedList<IActivationPathNode> GetWrappers(IInjectionScope scope, Type type, IActivationExpressionRequest request, out Type wrappedType);
    }

    /// <summary>
    /// Creates linq expressions for wrappers
    /// </summary>
    public class WrapperExpressionCreator : IWrapperExpressionCreator
    {
        /// <summary>
        /// Get an activation expression for a request
        /// </summary>
        /// <param name="scope">scope for request</param>
        /// <param name="request">request</param>
        /// <returns></returns>
        public IActivationExpressionResult GetActivationExpression(IInjectionScope scope, IActivationExpressionRequest request)
        {
            Type wrappedType;

            var wrappers = GetWrappers(scope, request.ActivationType, request, out wrappedType);

            if (wrappers != ImmutableLinkedList<IActivationPathNode>.Empty)
            {
                if (request.DecoratorPathNode != null &&
                    wrappedType.GetTypeInfo().IsAssignableFrom(request.DecoratorPathNode.Strategy.ActivationType.GetTypeInfo()))
                {
                    var decorator = request.PopDecoratorPathNode();

                    wrappers = ImmutableLinkedList<IActivationPathNode>.Empty.Add(decorator).AddRange(wrappers.Reverse());
                }
                else
                {
                    var collection = scope.StrategyCollectionContainer.GetActivationStrategyCollection(wrappedType);

                    if (collection == null && wrappedType.IsConstructedGenericType)
                    {
                        var generic = wrappedType.GetGenericTypeDefinition();

                        collection = scope.StrategyCollectionContainer.GetActivationStrategyCollection(generic);
                    }

                    if (collection != null)
                    {
                        var primary = request.Filter == null ? collection.GetPrimary() : null;

                        if (primary != null)
                        {
                            wrappers = ImmutableLinkedList<IActivationPathNode>.Empty.Add(new WrapperActivationPathNode(primary, wrappedType, null)).AddRange(wrappers.Reverse());
                        }
                        else
                        {
                            foreach (var strategy in collection.GetStrategies())
                            {
                                bool pass = true;

                                if (strategy.HasConditions)
                                {
                                    foreach (var condition in strategy.Conditions)
                                    {
                                        if (!condition.MeetsCondition(strategy, request.GetStaticInjectionContext(), null))
                                        {
                                            pass = false;
                                            break;
                                        }
                                    }
                                }

                                if (pass &&
                                    (request.Filter == null || request.Filter(strategy)))
                                {
                                    wrappers = ImmutableLinkedList<IActivationPathNode>.Empty.Add(new WrapperActivationPathNode(strategy, wrappedType, null)).AddRange(wrappers.Reverse());
                                }
                            }
                        }
                    }
                    else
                    {
                        return null;
                    }
                }

                request.SetWrapperPath(wrappers);

                var wrapper = request.PopWrapperPathNode();

                return wrapper.GetActivationExpression(scope, request);
            }

            return null;
        }

        /// <summary>
        /// Get wrappers for a request
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="type"></param>
        /// <param name="request"></param>
        /// <param name="wrappedType"></param>
        /// <returns></returns>
        public ImmutableLinkedList<IActivationPathNode> GetWrappers(IInjectionScope scope, Type type, IActivationExpressionRequest request, out Type wrappedType)
        {
            var wrapperCollection = scope.WrapperCollectionContainer.GetActivationStrategyCollection(type);

            if (wrapperCollection == null && type.IsConstructedGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();

                wrapperCollection = scope.WrapperCollectionContainer.GetActivationStrategyCollection(genericType);
            }

            if (wrapperCollection != null)
            {
                var strategy = request.Filter == null ? wrapperCollection.GetPrimary() : null;

                if (strategy == null)
                {
                    foreach (var s in wrapperCollection.GetStrategies())
                    {
                        bool pass = true;
                        if (s.HasConditions)
                        {
                            foreach (var condition in s.Conditions)
                            {
                                if (!condition.MeetsCondition(s, request.GetStaticInjectionContext(), null))
                                {
                                    pass = false;
                                }
                            }
                        }

                        if (pass)
                        {
                            strategy = s;
                            break;
                        }
                    }
                }

                if (strategy != null)
                {
                    var newType = strategy.GetWrappedType(type);
                    
                    if (newType == null)
                    {
                        throw new Exception("Wrapper strategy returned null for wrapped type, " +
                                            strategy.GetType().FullName);
                    }

                    return GetWrappers(scope, newType, request, out wrappedType).Add(new WrapperActivationPathNode(strategy, type, null));
                }
            }

            wrappedType = type;

            return ImmutableLinkedList<IActivationPathNode>.Empty;
        }
    }
}
