// ****************************************************************************
// <copyright file="SimpleIoc.cs" company="GalaSoft Laurent Bugnion">
// Copyright © GalaSoft Laurent Bugnion 2011-2016
// </copyright>
// ****************************************************************************
// <author>Laurent Bugnion</author>
// <email>laurent@galasoft.ch</email>
// <date>10.4.2011</date>
// <project>GalaSoft.MvvmLight.Extras</project>
// <web>http://www.mvvmlight.net</web>
// <license>
// See license.txt in this project or http://www.galasoft.ch/license_MIT.txt
// </license>
// <LastBaseLevel>BL0005</LastBaseLevel>
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Practices.ServiceLocation;

namespace XTC.UITestReproduction
{
    /// <summary>
    ///     A very simple IOC container with basic functionality needed to register and resolve
    ///     instances. If needed, this class can be replaced by another more elaborate
    ///     IOC container implementing the IServiceLocator interface.
    ///     The inspiration for this class is at https://gist.github.com/716137 but it has
    ///     been extended with additional features.
    /// </summary>
    //// [ClassInfo(typeof(SimpleIoc),
    ////  VersionString = "5.1.9",
    ////  DateString = "201502072030",
    ////  Description = "A very simple IOC container.",
    ////  UrlContacts = "http://www.galasoft.ch/contact_en.html",
    ////  Email = "laurent@galasoft.ch")]
    [SuppressMessage(
        "Microsoft.Naming",
        "CA1704:IdentifiersShouldBeSpelledCorrectly",
        MessageId = "Ioc")]
    public class SimpleIoc : ISimpleIoc
    {
        private static SimpleIoc _default;

        private readonly Dictionary<Type, ConstructorInfo> constructorInfos
            = new Dictionary<Type, ConstructorInfo>();

        private readonly string defaultKey = Guid.NewGuid().ToString();

        private readonly object[] emptyArguments = new object[0];

        private readonly Dictionary<Type, Dictionary<string, Delegate>> factories
            = new Dictionary<Type, Dictionary<string, Delegate>>();

        private readonly Dictionary<Type, Dictionary<string, object>> instancesRegistry
            = new Dictionary<Type, Dictionary<string, object>>();

        private readonly Dictionary<Type, Type> interfaceToClassMap
            = new Dictionary<Type, Type>();

        private readonly object syncLock = new object();

        /// <summary>
        ///     This class' default instance.
        /// </summary>
        public static SimpleIoc Default
        {
            get { return _default ?? (_default = new SimpleIoc()); }
        }

        /// <summary>
        ///     Checks whether at least one instance of a given class is already created in the container.
        /// </summary>
        /// <typeparam name="TClass">The class that is queried.</typeparam>
        /// <returns>True if at least on instance of the class is already created, false otherwise.</returns>
        public bool ContainsCreated<TClass>()
        {
            return ContainsCreated<TClass>(null);
        }

        /// <summary>
        ///     Checks whether the instance with the given key is already created for a given class
        ///     in the container.
        /// </summary>
        /// <typeparam name="TClass">The class that is queried.</typeparam>
        /// <param name="key">The key that is queried.</param>
        /// <returns>
        ///     True if the instance with the given key is already registered for the given class,
        ///     false otherwise.
        /// </returns>
        public bool ContainsCreated<TClass>(string key)
        {
            var classType = typeof(TClass);

            if (!instancesRegistry.ContainsKey(classType))
            {
                return false;
            }

            if (string.IsNullOrEmpty(key))
            {
                return instancesRegistry[classType].Count > 0;
            }

            return instancesRegistry[classType].ContainsKey(key);
        }

        /// <summary>
        ///     Gets a value indicating whether a given type T is already registered.
        /// </summary>
        /// <typeparam name="T">The type that the method checks for.</typeparam>
        /// <returns>True if the type is registered, false otherwise.</returns>
        public bool IsRegistered<T>()
        {
            var classType = typeof(T);
            return interfaceToClassMap.ContainsKey(classType);
        }

        /// <summary>
        ///     Gets a value indicating whether a given type T and a give key
        ///     are already registered.
        /// </summary>
        /// <typeparam name="T">The type that the method checks for.</typeparam>
        /// <param name="key">The key that the method checks for.</param>
        /// <returns>True if the type and key are registered, false otherwise.</returns>
        public bool IsRegistered<T>(string key)
        {
            var classType = typeof(T);

            if (!interfaceToClassMap.ContainsKey(classType)
                || !factories.ContainsKey(classType))
            {
                return false;
            }

            return factories[classType].ContainsKey(key);
        }

        /// <summary>
        ///     Registers a given type for a given interface.
        /// </summary>
        /// <typeparam name="TInterface">The interface for which instances will be resolved.</typeparam>
        /// <typeparam name="TClass">The type that must be used to create instances.</typeparam>
        [SuppressMessage(
            "Microsoft.Design",
            "CA1004",
            Justification = "This syntax is better than the alternatives.")]
        public void Register<TInterface, TClass>()
            where TClass : class
            where TInterface : class
        {
            Register<TInterface, TClass>(false);
        }

        /// <summary>
        ///     Registers a given type for a given interface with the possibility for immediate
        ///     creation of the instance.
        /// </summary>
        /// <typeparam name="TInterface">The interface for which instances will be resolved.</typeparam>
        /// <typeparam name="TClass">The type that must be used to create instances.</typeparam>
        /// <param name="createInstanceImmediately">
        ///     If true, forces the creation of the default
        ///     instance of the provided class.
        /// </param>
        [SuppressMessage(
            "Microsoft.Design",
            "CA1004",
            Justification = "This syntax is better than the alternatives.")]
        public void Register<TInterface, TClass>(bool createInstanceImmediately)
            where TClass : class
            where TInterface : class
        {
            lock (syncLock)
            {
                var interfaceType = typeof(TInterface);
                var classType = typeof(TClass);

                if (interfaceToClassMap.ContainsKey(interfaceType))
                {
                    if (interfaceToClassMap[interfaceType] != classType)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "There is already a class registered for {0}.",
                                interfaceType.FullName));
                    }
                }
                else
                {
                    interfaceToClassMap.Add(interfaceType, classType);
                    constructorInfos.Add(classType, GetConstructorInfo(classType));
                }

                Func<TInterface> factory = MakeInstance<TInterface>;
                DoRegister(interfaceType, factory, defaultKey);

                if (createInstanceImmediately)
                {
                    GetInstance<TInterface>();
                }
            }
        }

        /// <summary>
        ///     Registers a given type.
        /// </summary>
        /// <typeparam name="TClass">The type that must be used to create instances.</typeparam>
        [SuppressMessage(
            "Microsoft.Design",
            "CA1004",
            Justification = "This syntax is better than the alternatives.")]
        public void Register<TClass>()
            where TClass : class
        {
            Register<TClass>(false);
        }

        /// <summary>
        ///     Registers a given type with the possibility for immediate
        ///     creation of the instance.
        /// </summary>
        /// <typeparam name="TClass">The type that must be used to create instances.</typeparam>
        /// <param name="createInstanceImmediately">
        ///     If true, forces the creation of the default
        ///     instance of the provided class.
        /// </param>
        [SuppressMessage(
            "Microsoft.Design",
            "CA1004",
            Justification = "This syntax is better than the alternatives.")]
        public void Register<TClass>(bool createInstanceImmediately)
            where TClass : class
        {
            var classType = typeof(TClass);
            if (classType.GetTypeInfo().IsInterface)
            {
                throw new ArgumentException("An interface cannot be registered alone.");
            }

            lock (syncLock)
            {
                if (factories.ContainsKey(classType)
                    && factories[classType].ContainsKey(defaultKey))
                {
                    if (!constructorInfos.ContainsKey(classType))
                    {
                        // Throw only if constructorinfos have not been
                        // registered, which means there is a default factory
                        // for this class.
                        throw new InvalidOperationException(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Class {0} is already registered.",
                                classType));
                    }

                    return;
                }

                if (!interfaceToClassMap.ContainsKey(classType))
                {
                    interfaceToClassMap.Add(classType, null);
                }

                constructorInfos.Add(classType, GetConstructorInfo(classType));
                Func<TClass> factory = MakeInstance<TClass>;
                DoRegister(classType, factory, defaultKey);

                if (createInstanceImmediately)
                {
                    GetInstance<TClass>();
                }
            }
        }

        /// <summary>
        ///     Registers a given instance for a given type.
        /// </summary>
        /// <typeparam name="TClass">The type that is being registered.</typeparam>
        /// <param name="factory">
        ///     The factory method able to create the instance that
        ///     must be returned when the given type is resolved.
        /// </param>
        public void Register<TClass>(Func<TClass> factory)
            where TClass : class
        {
            Register(factory, false);
        }

        /// <summary>
        ///     Registers a given instance for a given type with the possibility for immediate
        ///     creation of the instance.
        /// </summary>
        /// <typeparam name="TClass">The type that is being registered.</typeparam>
        /// <param name="factory">
        ///     The factory method able to create the instance that
        ///     must be returned when the given type is resolved.
        /// </param>
        /// <param name="createInstanceImmediately">
        ///     If true, forces the creation of the default
        ///     instance of the provided class.
        /// </param>
        public void Register<TClass>(Func<TClass> factory, bool createInstanceImmediately)
            where TClass : class
        {
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }

            lock (syncLock)
            {
                var classType = typeof(TClass);

                if (factories.ContainsKey(classType)
                    && factories[classType].ContainsKey(defaultKey))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "There is already a factory registered for {0}.",
                            classType.FullName));
                }

                if (!interfaceToClassMap.ContainsKey(classType))
                {
                    interfaceToClassMap.Add(classType, null);
                }

                DoRegister(classType, factory, defaultKey);

                if (createInstanceImmediately)
                {
                    GetInstance<TClass>();
                }
            }
        }

        /// <summary>
        ///     Registers a given instance for a given type and a given key.
        /// </summary>
        /// <typeparam name="TClass">The type that is being registered.</typeparam>
        /// <param name="factory">
        ///     The factory method able to create the instance that
        ///     must be returned when the given type is resolved.
        /// </param>
        /// <param name="key">The key for which the given instance is registered.</param>
        public void Register<TClass>(Func<TClass> factory, string key)
            where TClass : class
        {
            Register(factory, key, false);
        }

        /// <summary>
        ///     Registers a given instance for a given type and a given key with the possibility for immediate
        ///     creation of the instance.
        /// </summary>
        /// <typeparam name="TClass">The type that is being registered.</typeparam>
        /// <param name="factory">
        ///     The factory method able to create the instance that
        ///     must be returned when the given type is resolved.
        /// </param>
        /// <param name="key">The key for which the given instance is registered.</param>
        /// <param name="createInstanceImmediately">
        ///     If true, forces the creation of the default
        ///     instance of the provided class.
        /// </param>
        public void Register<TClass>(
            Func<TClass> factory,
            string key,
            bool createInstanceImmediately)
            where TClass : class
        {
            lock (syncLock)
            {
                var classType = typeof(TClass);

                if (factories.ContainsKey(classType)
                    && factories[classType].ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "There is already a factory registered for {0} with key {1}.",
                            classType.FullName,
                            key));
                }

                if (!interfaceToClassMap.ContainsKey(classType))
                {
                    interfaceToClassMap.Add(classType, null);
                }

                DoRegister(classType, factory, key);

                if (createInstanceImmediately)
                {
                    GetInstance<TClass>(key);
                }
            }
        }

        /// <summary>
        ///     Resets the instance in its original states. This deletes all the
        ///     registrations.
        /// </summary>
        public void Reset()
        {
            interfaceToClassMap.Clear();
            instancesRegistry.Clear();
            constructorInfos.Clear();
            factories.Clear();
        }

        /// <summary>
        ///     Unregisters a class from the cache and removes all the previously
        ///     created instances.
        /// </summary>
        /// <typeparam name="TClass">The class that must be removed.</typeparam>
        [SuppressMessage(
            "Microsoft.Design",
            "CA1004",
            Justification = "This syntax is better than the alternatives.")]
        public void Unregister<TClass>()
            where TClass : class
        {
            lock (syncLock)
            {
                var serviceType = typeof(TClass);
                Type resolveTo;

                if (interfaceToClassMap.ContainsKey(serviceType))
                {
                    resolveTo = interfaceToClassMap[serviceType] ?? serviceType;
                }
                else
                {
                    resolveTo = serviceType;
                }

                if (instancesRegistry.ContainsKey(serviceType))
                {
                    instancesRegistry.Remove(serviceType);
                }

                if (interfaceToClassMap.ContainsKey(serviceType))
                {
                    interfaceToClassMap.Remove(serviceType);
                }

                if (factories.ContainsKey(serviceType))
                {
                    factories.Remove(serviceType);
                }

                if (constructorInfos.ContainsKey(resolveTo))
                {
                    constructorInfos.Remove(resolveTo);
                }
            }
        }

        /// <summary>
        ///     Removes the given instance from the cache. The class itself remains
        ///     registered and can be used to create other instances.
        /// </summary>
        /// <typeparam name="TClass">The type of the instance to be removed.</typeparam>
        /// <param name="instance">The instance that must be removed.</param>
        public void Unregister<TClass>(TClass instance)
            where TClass : class
        {
            lock (syncLock)
            {
                var classType = typeof(TClass);

                if (instancesRegistry.ContainsKey(classType))
                {
                    var list = instancesRegistry[classType];

                    var pairs = list.Where(pair => pair.Value == instance).ToList();
                    for (var index = 0; index < pairs.Count(); index++)
                    {
                        var key = pairs[index].Key;

                        list.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        ///     Removes the instance corresponding to the given key from the cache. The class itself remains
        ///     registered and can be used to create other instances.
        /// </summary>
        /// <typeparam name="TClass">The type of the instance to be removed.</typeparam>
        /// <param name="key">The key corresponding to the instance that must be removed.</param>
        [SuppressMessage(
            "Microsoft.Design",
            "CA1004",
            Justification = "This syntax is better than the alternatives.")]
        public void Unregister<TClass>(string key)
            where TClass : class
        {
            lock (syncLock)
            {
                var classType = typeof(TClass);

                if (instancesRegistry.ContainsKey(classType))
                {
                    var list = instancesRegistry[classType];

                    var pairs = list.Where(pair => pair.Key == key).ToList();
                    for (var index = 0; index < pairs.Count(); index++)
                    {
                        list.Remove(pairs[index].Key);
                    }
                }

                if (factories.ContainsKey(classType))
                {
                    if (factories[classType].ContainsKey(key))
                    {
                        factories[classType].Remove(key);
                    }
                }
            }
        }

        #region Implementation of IServiceProvider

        /// <summary>
        ///     Gets the service object of the specified type.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type serviceType has not
        ///     been registered before calling this method.
        /// </exception>
        /// <returns>
        ///     A service object of type <paramref name="serviceType" />.
        /// </returns>
        /// <param name="serviceType">An object that specifies the type of service object to get.</param>
        public object GetService(Type serviceType)
        {
            return DoGetService(serviceType, defaultKey);
        }

        #endregion

        private object DoGetService(Type serviceType, string key, bool cache = true)
        {
            lock (syncLock)
            {
                if (string.IsNullOrEmpty(key))
                {
                    key = defaultKey;
                }

                Dictionary<string, object> instances = null;

                if (!instancesRegistry.ContainsKey(serviceType))
                {
                    if (!interfaceToClassMap.ContainsKey(serviceType))
                    {
                        throw new ActivationException(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Type not found in cache: {0}.",
                                serviceType.FullName));
                    }

                    if (cache)
                    {
                        instances = new Dictionary<string, object>();
                        instancesRegistry.Add(serviceType, instances);
                    }
                }
                else
                {
                    instances = instancesRegistry[serviceType];
                }

                if (instances != null
                    && instances.ContainsKey(key))
                {
                    return instances[key];
                }

                object instance = null;

                if (factories.ContainsKey(serviceType))
                {
                    if (factories[serviceType].ContainsKey(key))
                    {
                        instance = factories[serviceType][key].DynamicInvoke(null);
                    }
                    else
                    {
                        if (factories[serviceType].ContainsKey(defaultKey))
                        {
                            instance = factories[serviceType][defaultKey].DynamicInvoke(null);
                        }
                        else
                        {
                            throw new ActivationException(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Type not found in cache without a key: {0}",
                                    serviceType.FullName));
                        }
                    }
                }

                if (cache
                    && instances != null)
                {
                    instances.Add(key, instance);
                }

                return instance;
            }
        }

        private void DoRegister<TClass>(Type classType, Func<TClass> factory, string key)
        {
            if (factories.ContainsKey(classType))
            {
                if (factories[classType].ContainsKey(key))
                {
                    // The class is already registered, ignore and continue.
                    return;
                }

                factories[classType].Add(key, factory);
            }
            else
            {
                var list = new Dictionary<string, Delegate>
                {
                    {
                        key,
                        factory
                    }
                };

                factories.Add(classType, list);
            }
        }

        private ConstructorInfo GetConstructorInfo(Type serviceType)
        {
            Type resolveTo;

            if (interfaceToClassMap.ContainsKey(serviceType))
            {
                resolveTo = interfaceToClassMap[serviceType] ?? serviceType;
            }
            else
            {
                resolveTo = serviceType;
            }

            var constructorInfos = resolveTo.GetTypeInfo().DeclaredConstructors.Where(c => c.IsPublic).ToArray();

            if (constructorInfos.Length > 1)
            {
                if (constructorInfos.Length > 2)
                {
                    return GetPreferredConstructorInfo(constructorInfos, resolveTo);
                }

                if (constructorInfos.FirstOrDefault(i => i.Name == ".cctor") == null)
                {
                    return GetPreferredConstructorInfo(constructorInfos, resolveTo);
                }

                var first = constructorInfos.FirstOrDefault(i => i.Name != ".cctor");

                if (first == null
                    || !first.IsPublic)
                {
                    throw new ActivationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Cannot register: No public constructor found in {0}.",
                            resolveTo.Name));
                }

                return first;
            }

            if (constructorInfos.Length == 0
                || (constructorInfos.Length == 1
                    && !constructorInfos[0].IsPublic))
            {
                throw new ActivationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Cannot register: No public constructor found in {0}.",
                        resolveTo.Name));
            }

            return constructorInfos[0];
        }

        [SuppressMessage(
            "Microsoft.Naming",
            "CA2204:Literals should be spelled correctly",
            MessageId = "PreferredConstructor")]
        private static ConstructorInfo GetPreferredConstructorInfo(IEnumerable<ConstructorInfo> constructorInfos,
            Type resolveTo)
        {
            var preferredConstructorInfo
                = (from t in constructorInfos
                    let attribute = t.GetCustomAttribute(typeof(PreferredConstructorAttribute))
                    where attribute != null
                    select t).FirstOrDefault();

            if (preferredConstructorInfo == null)
            {
                throw new ActivationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Cannot register: Multiple constructors found in {0} but none marked with PreferredConstructor.",
                        resolveTo.Name));
            }

            return preferredConstructorInfo;
        }

        private TClass MakeInstance<TClass>()
        {
            var serviceType = typeof(TClass);

            var constructor = constructorInfos.ContainsKey(serviceType)
                ? constructorInfos[serviceType]
                : GetConstructorInfo(serviceType);

            var parameterInfos = constructor.GetParameters();

            if (parameterInfos.Length == 0)
            {
                return (TClass) constructor.Invoke(emptyArguments);
            }

            var parameters = new object[parameterInfos.Length];

            foreach (var parameterInfo in parameterInfos)
            {
                parameters[parameterInfo.Position] = GetService(parameterInfo.ParameterType);
            }

            return (TClass) constructor.Invoke(parameters);
        }

        /// <summary>
        ///     Provides a way to get all the created instances of a given type available in the
        ///     cache. Registering a class or a factory does not automatically
        ///     create the corresponding instance! To create an instance, either register
        ///     the class or the factory with createInstanceImmediately set to true,
        ///     or call the GetInstance method before calling GetAllCreatedInstances.
        ///     Alternatively, use the GetAllInstances method, which auto-creates default
        ///     instances for all registered classes.
        /// </summary>
        /// <param name="serviceType">
        ///     The class of which all instances
        ///     must be returned.
        /// </param>
        /// <returns>All the already created instances of the given type.</returns>
        public IEnumerable<object> GetAllCreatedInstances(Type serviceType)
        {
            if (instancesRegistry.ContainsKey(serviceType))
            {
                return instancesRegistry[serviceType].Values;
            }

            return new List<object>();
        }

        /// <summary>
        ///     Provides a way to get all the created instances of a given type available in the
        ///     cache. Registering a class or a factory does not automatically
        ///     create the corresponding instance! To create an instance, either register
        ///     the class or the factory with createInstanceImmediately set to true,
        ///     or call the GetInstance method before calling GetAllCreatedInstances.
        ///     Alternatively, use the GetAllInstances method, which auto-creates default
        ///     instances for all registered classes.
        /// </summary>
        /// <typeparam name="TService">
        ///     The class of which all instances
        ///     must be returned.
        /// </typeparam>
        /// <returns>All the already created instances of the given type.</returns>
        public IEnumerable<TService> GetAllCreatedInstances<TService>()
        {
            var serviceType = typeof(TService);
            return GetAllCreatedInstances(serviceType)
                .Select(instance => (TService) instance);
        }

        #region Implementation of IServiceLocator

        /// <summary>
        ///     Provides a way to get all the created instances of a given type available in the
        ///     cache. Calling this method auto-creates default
        ///     instances for all registered classes.
        /// </summary>
        /// <param name="serviceType">
        ///     The class of which all instances
        ///     must be returned.
        /// </param>
        /// <returns>All the instances of the given type.</returns>
        public IEnumerable<object> GetAllInstances(Type serviceType)
        {
            lock (factories)
            {
                if (factories.ContainsKey(serviceType))
                {
                    foreach (var factory in factories[serviceType])
                    {
                        GetInstance(serviceType, factory.Key);
                    }
                }
            }

            if (instancesRegistry.ContainsKey(serviceType))
            {
                return instancesRegistry[serviceType].Values;
            }


            return new List<object>();
        }

        /// <summary>
        ///     Provides a way to get all the created instances of a given type available in the
        ///     cache. Calling this method auto-creates default
        ///     instances for all registered classes.
        /// </summary>
        /// <typeparam name="TService">
        ///     The class of which all instances
        ///     must be returned.
        /// </typeparam>
        /// <returns>All the instances of the given type.</returns>
        public IEnumerable<TService> GetAllInstances<TService>()
        {
            var serviceType = typeof(TService);
            return GetAllInstances(serviceType)
                .Select(instance => (TService) instance);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type. If no instance had been instantiated
        ///     before, a new instance will be created. If an instance had already
        ///     been created, that same instance will be returned.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type serviceType has not
        ///     been registered before calling this method.
        /// </exception>
        /// <param name="serviceType">
        ///     The class of which an instance
        ///     must be returned.
        /// </param>
        /// <returns>An instance of the given type.</returns>
        public object GetInstance(Type serviceType)
        {
            return DoGetService(serviceType, defaultKey);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type. This method
        ///     always returns a new instance and doesn't cache it in the IOC container.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type serviceType has not
        ///     been registered before calling this method.
        /// </exception>
        /// <param name="serviceType">
        ///     The class of which an instance
        ///     must be returned.
        /// </param>
        /// <returns>An instance of the given type.</returns>
        public object GetInstanceWithoutCaching(Type serviceType)
        {
            return DoGetService(serviceType, defaultKey, false);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type corresponding
        ///     to a given key. If no instance had been instantiated with this
        ///     key before, a new instance will be created. If an instance had already
        ///     been created with the same key, that same instance will be returned.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type serviceType has not
        ///     been registered before calling this method.
        /// </exception>
        /// <param name="serviceType">The class of which an instance must be returned.</param>
        /// <param name="key">The key uniquely identifying this instance.</param>
        /// <returns>An instance corresponding to the given type and key.</returns>
        public object GetInstance(Type serviceType, string key)
        {
            return DoGetService(serviceType, key);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type. This method
        ///     always returns a new instance and doesn't cache it in the IOC container.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type serviceType has not
        ///     been registered before calling this method.
        /// </exception>
        /// <param name="serviceType">The class of which an instance must be returned.</param>
        /// <param name="key">The key uniquely identifying this instance.</param>
        /// <returns>An instance corresponding to the given type and key.</returns>
        public object GetInstanceWithoutCaching(Type serviceType, string key)
        {
            return DoGetService(serviceType, key, false);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type. If no instance had been instantiated
        ///     before, a new instance will be created. If an instance had already
        ///     been created, that same instance will be returned.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type TService has not
        ///     been registered before calling this method.
        /// </exception>
        /// <typeparam name="TService">
        ///     The class of which an instance
        ///     must be returned.
        /// </typeparam>
        /// <returns>An instance of the given type.</returns>
        public TService GetInstance<TService>()
        {
            return (TService) DoGetService(typeof(TService), defaultKey);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type. This method
        ///     always returns a new instance and doesn't cache it in the IOC container.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type TService has not
        ///     been registered before calling this method.
        /// </exception>
        /// <typeparam name="TService">
        ///     The class of which an instance
        ///     must be returned.
        /// </typeparam>
        /// <returns>An instance of the given type.</returns>
        public TService GetInstanceWithoutCaching<TService>()
        {
            return (TService) DoGetService(typeof(TService), defaultKey, false);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type corresponding
        ///     to a given key. If no instance had been instantiated with this
        ///     key before, a new instance will be created. If an instance had already
        ///     been created with the same key, that same instance will be returned.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type TService has not
        ///     been registered before calling this method.
        /// </exception>
        /// <typeparam name="TService">The class of which an instance must be returned.</typeparam>
        /// <param name="key">The key uniquely identifying this instance.</param>
        /// <returns>An instance corresponding to the given type and key.</returns>
        public TService GetInstance<TService>(string key)
        {
            return (TService) DoGetService(typeof(TService), key);
        }

        /// <summary>
        ///     Provides a way to get an instance of a given type. This method
        ///     always returns a new instance and doesn't cache it in the IOC container.
        /// </summary>
        /// <exception cref="ActivationException">
        ///     If the type TService has not
        ///     been registered before calling this method.
        /// </exception>
        /// <typeparam name="TService">The class of which an instance must be returned.</typeparam>
        /// <param name="key">The key uniquely identifying this instance.</param>
        /// <returns>An instance corresponding to the given type and key.</returns>
        public TService GetInstanceWithoutCaching<TService>(string key)
        {
            return (TService) DoGetService(typeof(TService), key, false);
        }

        #endregion
    }
}