﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace Magpie.Compilation
{
    public class Compiler
    {
        public FunctionTable Functions { get { return mFunctions; } }
        public TypeTable Types { get { return mTypes; } }

        public Compiler(IForeignStaticInterface foreignInterface)
        {
            mForeignInterface = foreignInterface;
        }

        public void AddSourceFile(string filePath)
        {
            // defer the actual parsing until we compile. this way errors that occur
            // during source translation can generate compile errors at an expected
            // time.
            mSourcePaths.Add(filePath);
        }

        public IList<CompileError> Compile(Stream outputStream)
        {
            var errors = new List<CompileError>();

            try
            {
                // parse all the source files
                foreach (var path in mSourcePaths)
                {
                    SourceFile source = MagpieParser.ParseSourceFile(path);
                    AddNamespace(source.Name, source, source);
                }

                // build the function table
                mFunctions.AddRange(Intrinsic.All);
                mFunctions.AddRange(Intrinsic.AllGenerics);

                // bind the user-defined types and create their auto-generated functions
                foreach (var structure in mTypes.Structs)
                {
                    TypeBinder.Bind(this, structure);
                    mFunctions.AddRange(structure.BuildFunctions());
                }

                foreach (var union in mTypes.Unions)
                {
                    TypeBinder.Bind(this, union);
                    mFunctions.AddRange(union.BuildFunctions());
                }

                foreach (var structure in mTypes.GenericStructs)
                {
                    mFunctions.AddRange(structure.BuildFunctions());
                }

                foreach (var union in mTypes.GenericUnions)
                {
                    mFunctions.AddRange(union.BuildFunctions());
                }

                if (mForeignInterface != null)
                {
                    mFunctions.AddRange(mForeignInterface.Functions.Cast<ICallable>());
                }

                mFunctions.BindAll(this);
            }
            catch (CompileException ex)
            {
                errors.Add(new CompileError(CompileStage.Compile, ex.Position.Line, ex.Message));
            }

            if (errors.Count == 0)
            {
                BytecodeFile file = new BytecodeFile(this);
                file.Save(outputStream);
            }

            return errors;
        }

        /// <summary>
        /// Resolves and binds a reference to a name.
        /// </summary>
        /// <param name="function">The function being compiled.</param>
        /// <param name="scope">The scope in which the name is being bound.</param>
        /// <param name="name">The name being resolved. May or may not be fully-qualified.</param>
        /// <param name="typeArgs">The type arguments being applied to the name. For
        /// example, resolving "foo'(int, bool)" would pass in {int, bool} here.</param>
        /// <param name="arg">The argument being applied to the name.</param>
        /// <returns></returns>
        public IBoundExpr ResolveName(Function function,
            Scope scope, Position position, string name,
            IList<IUnboundDecl> typeArgs, IBoundExpr arg)
        {
            IBoundDecl argType = null;
            if (arg != null) argType = arg.Type;

            IBoundExpr resolved = null;

            // see if it's an argument
            if (function.ParamNames.Contains(name))
            {
                // load the argument
                resolved = new LoadExpr(new LocalsExpr(), function.ParameterType, 0);

                if (function.ParamNames.Count > 1)
                {
                    // function takes multiple parameters, so load it from the tuple
                    var paramTuple = (BoundTupleType)function.ParameterType;

                    var argIndex = (byte)function.ParamNames.IndexOf(name);
                    resolved = new LoadExpr(resolved, paramTuple.Fields[argIndex], argIndex);
                }
            }

            // see if it's a local
            if (scope.Contains(name))
            {
                var local = scope[name];

                // just load the value
                resolved = new LoadExpr(new LocalsExpr(), scope[name]);
            }

            // if we resolved to a local name, handle it
            if (resolved != null)
            {
                if (typeArgs.Count > 0) throw new CompileException(position, "Cannot apply type arguments to a local variable or function argument.");

                // if the local or argument is holding a function reference and we're passed args, call it
                if (argType != null)
                {
                    var funcType = resolved.Type as FuncType;

                    if (funcType != null)
                    {
                        // check that args match
                        if (!DeclComparer.TypesMatch(funcType.Parameter.Bound, argType))
                        {
                            throw new CompileException(position, "Argument types passed to local function reference do not match function's parameter types.");
                        }

                        // call it
                        resolved = new BoundCallExpr(resolved, arg);
                    }
                    else
                    {
                        // not calling a function, so try to desugar to a __Call
                        var callArg = new BoundTupleExpr(new IBoundExpr[] { resolved, arg });

                        resolved = ResolveFunction(function, position,
                            "__Call", new IUnboundDecl[0], callArg);

                        if (resolved == null) throw new CompileException(position, "Cannot call a local variable or argument that is not a function reference, and could not find a matching __Call.");
                    }
                }

                return resolved;
            }

            // implicitly apply () as the argument if no other argument was provided.
            // note that we do this *after* checking for locals because locals aren't
            // implicitly applied. since most locals aren't functions anyway, it won't
            // matter in most cases, and in cases where a local holds a function, the
            // user will mostly likely want to treat that function like a value: return
            // it, pass it around, etc.
            if (arg == null)
            {
                arg = new UnitExpr(Position.None);
                argType = arg.Type;
            }

            return ResolveFunction(function, position, name, typeArgs, arg);
        }

        public IBoundExpr ResolveFunction(Function function,
            Position position, string name,
            IList<IUnboundDecl> typeArgs, IBoundExpr arg)
        {
            if (arg == null) throw new ArgumentNullException("arg");

            // look up the function
            var callable = Functions.Find(this, function.SearchSpace, name, typeArgs, arg.Type);

            if (callable == null) throw new CompileException(position, String.Format("Could not resolve name {0}.",
                Callable.UniqueName(name, null, arg.Type)));

            return callable.CreateCall(arg);
        }

        /// <summary>
        /// Gets whether or not the given name within the given function and scope
        /// represents a local variable (or function parameter).
        /// </summary>
        /// <param name="function">The function in whose body the name is being
        /// looked up.</param>
        /// <param name="scope">The current local variable scope.</param>
        /// <param name="name">The name to look up.</param>
        /// <returns><c>true</c> if the name is a local variable or function
        /// parameter.</returns>
        public bool IsLocal(Function function,
            Scope scope, string name)
        {
            // see if it's an argument
            if (function.ParamNames.Contains(name)) return true;

            // see if it's a local
            if (scope.Contains(name)) return true;

            return false;
        }

        private void AddNamespace(string parentName, SourceFile file, Namespace namespaceObj)
        {
            var searchSpace = new NameSearchSpace(parentName, file.UsingNamespaces);

            foreach (var function in namespaceObj.Functions)
            {
                function.BindSearchSpace(searchSpace);
                mFunctions.AddUnbound(function);
            }

            foreach (var structure in namespaceObj.Structs)
            {
                structure.BindSearchSpace(searchSpace);
                mTypes.Add(structure);
            }

            foreach (var union in namespaceObj.Unions)
            {
                union.BindSearchSpace(searchSpace);
                mTypes.Add(union);
            }

            foreach (var function in namespaceObj.GenericFunctions)
            {
                function.BaseType.BindSearchSpace(searchSpace);
                mFunctions.Add(function);
            }

            foreach (var structure in namespaceObj.GenericStructs)
            {
                structure.BaseType.BindSearchSpace(searchSpace);
                mTypes.Add(structure);
            }

            foreach (var union in namespaceObj.GenericUnions)
            {
                union.BaseType.BindSearchSpace(searchSpace);
                mTypes.Add(union);
            }

            foreach (var childNamespace in namespaceObj.Namespaces)
            {
                var name = NameSearchSpace.Qualify(parentName, childNamespace.Name);
                AddNamespace(name, file, childNamespace);
            }
        }

        private readonly List<string> mSourcePaths = new List<string>();

        private readonly FunctionTable mFunctions = new FunctionTable();
        private readonly TypeTable mTypes = new TypeTable();
        private readonly IForeignStaticInterface mForeignInterface;
    }
}
