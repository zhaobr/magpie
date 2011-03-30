package com.stuffwithstuff.magpie.interpreter;

import com.stuffwithstuff.magpie.ast.Expr;

public class EnvironmentBuilder {
  public static void initialize(Interpreter interpreter) {
    EnvironmentBuilder builder = new EnvironmentBuilder(interpreter);
    builder.initialize();
  }
  
  public void initialize() {
    // Define the core error classes.
    newClass("Error");
    
    newClass("BadCallError")
        .inherit("Error");
    
    newClass("NoMethodError")
        .inherit("Error");
    
    // Define the core AST classes. We set these up here instead of in base
    // because they will need to be available immediately by the parser so that
    // quotations can be converted to Magpie.
    newClass("Position")
        .field("file",       name("String"))
        .field("startLine",  name("Int"))
        .field("startCol",   name("Int"))
        .field("endLine",    name("Int"))
        .field("endCol",     name("Int"));

    newClass("AssignExpression")
        .field("position", name("Position"))
        .field("receiver", or(name("Expression"), name("Nothing")))
        .field("name",     name("String"))
        .field("value",    name("Expression"));
    
    newClass("BlockExpression")
        .field("expressions", list(name("Expression")))
        .field("catchExpression", or(name("Expression"), name("Nothing")));
    
    newClass("BoolExpression")
        .field("position", name("Position"))
        .field("value", name("Bool"));

    newClass("BreakExpression")
        .field("position", name("Position"));

    newClass("CallExpression")
        .field("target",   name("Expression"))
        .field("argument", name("Expression"));

    newClass("FunctionExpression")
        .field("position",     name("Position"))
        .field("pattern",      name("Pattern"))
        .field("body",         name("Expression"));

    newClass("IntExpression")
        .field("position", name("Position"))
        .field("value",    name("Int"));
    
    newClass("LoopExpression")
        .field("position", name("Position"))
        .field("body",     name("Expression"));

    newClass("MatchCase")
        .field("pattern", name("Pattern"))
        .field("body",    name("Expression"));

    newClass("MatchExpression")
        .field("position", name("Position"))
        .field("value",    name("Expression"))
        .field("cases",    list(name("MatchCase")));

    newClass("MessageExpression")
        .field("position", name("Position"))
        .field("receiver", or(name("Expression"), name("Nothing")))
        .field("name",     Expr.name("String"));
    
    newClass("NothingExpression")
        .field("position", name("Position"));

    newClass("QuotationExpression")
        .field("position", name("Position"))
        .field("body",     name("Expression"));
    
    newClass("RecordExpression")
        .field("position", name("Position"))
        .field("fields",   list(name("String"), name("Expression")));

    newClass("ReturnExpression")
        .field("position", name("Position"))
        .field("value",    name("Expression"));
    
    newClass("ScopeExpression")
        .field("body", name("Expression"));
    
    newClass("StringExpression")
        .field("position", name("Position"))
        .field("value",    name("String"));

    newClass("ThisExpression")
        .field("position", name("Position"));

    newClass("TupleExpression")
        .field("fields", list(name("Expression")));
    
    newClass("UnquoteExpression")
        .field("position", name("Position"))
        .field("body",     name("Expression"));
    
    newClass("UsingExpression")
        .field("position", name("Position"))
        .field("name",     name("String"));

    newClass("VariableExpression")
        .field("position", name("Position"))
        .field("pattern",  name("Pattern"))
        .field("value",    name("Expression"));
  }
  
  private EnvironmentBuilder(Interpreter interpreter) {
    mInterpreter = interpreter;
  }
  private ClassBuilder newClass(String name) {
    return new ClassBuilder(mInterpreter.createGlobalClass(name));
  }
  
  private Expr list(Expr... exprs) {
    Expr arg;
    if (exprs.length > 1) {
      arg = Expr.tuple(exprs);
    } else {
      arg = exprs[0];
    }
    
    return Expr.call(Expr.name("List"), arg);
  }
  
  private Expr name(String name) {
    return Expr.name(name);
  }
  
  private Expr or(Expr... exprs) {
    Expr result = exprs[0];
    for (int i = 1; i < exprs.length; i++) {
      result = Expr.message(exprs[i].getPosition(), null, "|", Expr.tuple(result, exprs[i]));
    }
    return result;
  }

  private class ClassBuilder {
    public ClassBuilder(ClassObj classObj) {
      mClassObj = classObj;
    }
    
    public ClassBuilder inherit(String name) {
      Obj parent = mInterpreter.getGlobal(name);
      mClassObj.getParents().add(parent);
      return this;
    }
    
    public ClassBuilder field(String name, Expr type) {
      mClassObj.declareField(name, type);
      return this;
    }
    
    private ClassObj mClassObj;
  }
  
  private final Interpreter mInterpreter;
}
