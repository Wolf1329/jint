using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Jint.Native;
using Jint.Runtime.Interop;
using Jint.Tests.Runtime.Domain;
using Xunit;

namespace Jint.Tests.Runtime
{
    public partial class InteropTests
    {
        [Fact]
        public void ShouldHideSpecificMembers()
        {
            var engine = new Engine(options => options.SetMemberAccessor((e, target, member) =>
            {
                if (target is HiddenMembers)
                {
                    if (member == nameof(HiddenMembers.Member2) || member == nameof(HiddenMembers.Method2))
                    {
                        return JsValue.Undefined;
                    }
                }

                return null;
            }));

            engine.SetValue("m", new HiddenMembers());

            Assert.Equal("Member1", engine.Evaluate("m.Member1").ToString());
            Assert.Equal("undefined", engine.Evaluate("m.Member2").ToString());
            Assert.Equal("Method1", engine.Evaluate("m.Method1()").ToString());
            // check the method itself, not its invokation as it would mean invoking "undefined"
            Assert.Equal("undefined", engine.Evaluate("m.Method2").ToString());
        }

        [Fact]
        public void ShouldOverrideMembers()
        {
            var engine = new Engine(options => options.SetMemberAccessor((e, target, member) =>
            {
                if (target is HiddenMembers && member == nameof(HiddenMembers.Member1))
                {
                    return "Orange";
                }

                return null;
            }));

            engine.SetValue("m", new HiddenMembers());

            Assert.Equal("Orange", engine.Evaluate("m.Member1").ToString());
        }

        [Fact]
        public void ShouldBeAbleToFilterMembers()
        {
            var engine = new Engine(options => options
                .SetTypeResolver(new TypeResolver
                {
                    MemberFilter = member => !Attribute.IsDefined(member, typeof(ObsoleteAttribute))
                })
            );

            engine.SetValue("m", new HiddenMembers());

            Assert.True(engine.Evaluate("m.Field1").IsUndefined());
            Assert.True(engine.Evaluate("m.Member1").IsUndefined());
            Assert.True(engine.Evaluate("m.Method1").IsUndefined());

            Assert.True(engine.Evaluate("m.Field2").IsString());
            Assert.True(engine.Evaluate("m.Member2").IsString());
            Assert.True(engine.Evaluate("m.Method2()").IsString());

            // we forbid GetType by default
            Assert.True(engine.Evaluate("m.GetType").IsUndefined());
        }

        [Fact]
        public void ShouldBeAbleToExposeGetType()
        {
            var engine = new Engine(options =>
            {
                options.Interop.AllowGetType = true;
                options.Interop.AllowSystemReflection = true;
            });
            engine.SetValue("m", new HiddenMembers());
            Assert.True(engine.Evaluate("m.GetType").IsCallable);

            // reflection could bypass some safeguards
            Assert.Equal("Method1", engine.Evaluate("m.GetType().GetMethod('Method1').Invoke(m, [])").AsString());
        }

        [Fact]
        public void ShouldProtectFromReflectionServiceUsage()
        {
            var engine = new Engine();
            engine.SetValue("m", new HiddenMembers());

            // we can get a type reference if it's exposed via property, bypassing GetType
            var type = engine.Evaluate("m.Type");
            Assert.IsType<ObjectWrapper>(type);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.Evaluate("m.Type.Module.GetType().Module.GetType('System.DateTime')"));
            Assert.Equal("Cannot access System.Reflection namespace, check Engine's interop options", ex.Message);
        }

        [Fact]
        public void TypeReferenceShouldUseTypeResolverConfiguration()
        {
            var engine = new Engine(options =>
            {
                options.SetTypeResolver(new TypeResolver
                {
                    MemberFilter = member => !Attribute.IsDefined(member, typeof(ObsoleteAttribute))
                });
            });
            engine.SetValue("EchoService", TypeReference.CreateTypeReference(engine, typeof(EchoService)));
            Assert.Equal("anyone there", engine.Evaluate("EchoService.Echo('anyone there')").AsString());
            Assert.Equal("anyone there", engine.Evaluate("EchoService.echo('anyone there')").AsString());
            Assert.True(engine.Evaluate("EchoService.ECHO").IsUndefined());

            Assert.True(engine.Evaluate("EchoService.Hidden").IsUndefined());
        }

        [Fact]
        public void CustomDictionaryPropertyAccessTests()
        {
            var engine = new Engine(options =>
            {
                options.AllowClr();
            });

            var dc = new CustomDictionary<float>();
            dc["a"] = 1;

            engine.SetValue("dc", dc);

            Assert.Equal(1, engine.Evaluate("dc.a"));
            Assert.Equal(1, engine.Evaluate("dc['a']"));
            Assert.NotEqual(JsValue.Undefined, engine.Evaluate("dc.Add"));
            Assert.NotEqual(0, engine.Evaluate("dc.Add"));
            Assert.Equal(JsValue.Undefined, engine.Evaluate("dc.b"));

            engine.Execute("dc.b = 5");
            engine.Execute("dc.Add('c', 10)");
            Assert.Equal(5, engine.Evaluate("dc.b"));
            Assert.Equal(10, engine.Evaluate("dc.c"));
        }

        private static class EchoService
        {
            public static string Echo(string message) => message;

            [Obsolete]
            public static string Hidden(string message) => message;
        }

        private class CustomDictionary<TValue> : IDictionary<string, TValue>
        {
            Dictionary<string, TValue> _dictionary = new Dictionary<string, TValue>();

            public TValue this[string key]
            {
                get
                {
                    if (TryGetValue(key, out var val)) return val;

                    // Normally, dictionary Item accessor throws an error when key does not exist
                    // But this is a custom implementation
                    return default;
                }
                set
                {
                    _dictionary[key] = value;
                }
            }

            public ICollection<string> Keys => _dictionary.Keys;
            public ICollection<TValue> Values => _dictionary.Values;
            public int Count => _dictionary.Count;
            public bool IsReadOnly => false;
            public void Add(string key, TValue value) => _dictionary.Add(key, value);
            public void Add(KeyValuePair<string, TValue> item) => _dictionary.Add(item.Key, item.Value);
            public void Clear() => _dictionary.Clear();
            public bool Contains(KeyValuePair<string, TValue> item) => _dictionary.ContainsKey(item.Key);
            public bool ContainsKey(string key) => _dictionary.ContainsKey(key);
            public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex) => throw new NotImplementedException();
            public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => _dictionary.GetEnumerator();
            public bool Remove(string key) => _dictionary.Remove(key);
            public bool Remove(KeyValuePair<string, TValue> item) => _dictionary.Remove(item.Key);
            public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value) => _dictionary.TryGetValue(key, out value);
            IEnumerator IEnumerable.GetEnumerator() => _dictionary.GetEnumerator();
        }
    }
}
