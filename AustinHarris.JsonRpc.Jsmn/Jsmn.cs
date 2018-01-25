using System;
using System.Collections.Generic;
using System.Text;

namespace AustinHarris.JsonRpc.Jsmn
{


    /**
     * JSON type identifier. Basic types are:
     * 	o Object
     * 	o Array
     * 	o String
     * 	o Other primitive: number, boolean (true/false) or null
     */
    public enum jsmntype_t
    {
        JSMN_UNDEFINED = 0,
        JSMN_OBJECT = 1,
        JSMN_ARRAY = 2,
        JSMN_STRING = 3,
        JSMN_PRIMITIVE = 4
    }

    public enum jsmnerr
    {
        /* Not enough tokens were provided */
        JSMN_ERROR_NOMEM = -1,
        /* Invalid character inside JSON string */
        JSMN_ERROR_INVAL = -2,
        /* The string is not a full JSON packet, more bytes expected */
        JSMN_ERROR_PART = -3
    }

    /**
     * JSON token description.
     * type		type (object, array, string etc.)
     * start	start position in JSON data string
     * end		end position in JSON data string
     */
    public struct jsmntok_t
    {
        public jsmntype_t type;
        public int start;
        public int end;
        public int size;
        public int parent;
    };

    /**
     * JSON parser. Contains an array of token blocks available. Also stores
     * the string being parsed now and current position in that string
     */
    public struct jsmn_parser
    {

        public int pos; /* offset in the JSON string */
        public int toknext; /* next token to allocate */
        public int toksuper; /* superior token node, e.g parent object or array */
    };

    public unsafe static class jsmn_c
    {


        /**
     * Allocates a fresh unused token from the token pull.
     */
        unsafe static jsmntok_t* jsmn_alloc_token(jsmn_parser* parser,
            jsmntok_t* tokens, int num_tokens)
        {
            jsmntok_t* tok;
            if (parser->toknext >= num_tokens)
            {
                return null;
            }
            tok = &tokens[parser->toknext++];
            tok->start = tok->end = -1;
            tok->size = 0;

            tok->parent = -1;

            return tok;
        }

        /**
         * Fills token type and boundaries.
         */
        unsafe static void jsmn_fill_token(jsmntok_t* token, jsmntype_t type,
                                    int start, int end)
        {
            token->type = type;
            token->start = start;
            token->end = end;
            token->size = 0;
        }

        /**
         * Fills next available token with JSON primitive.
         */
        unsafe static int jsmn_parse_primitive(jsmn_parser* parser, char* js,
                int len, jsmntok_t* tokens, int num_tokens)
        {
            jsmntok_t* token;
            int start;

            start = parser->pos;

            for (; parser->pos < len && js[parser->pos] != '\0'; parser->pos++)
            {
                switch (js[parser->pos])
                {
                    //# ifndef JSMN_STRICT
                    //                /* In strict mode primitive must be followed by "," or "}" or "]" */
                    //                case ':':
                    //#endif
                    case '\t':
                    case '\r':
                    case '\n':
                    case ' ':
                    case ',':
                    case ']':
                    case '}':
                        goto found;
                }
                if (js[parser->pos] < 32 || js[parser->pos] >= 127)
                {
                    parser->pos = start;
                    return (int)jsmnerr.JSMN_ERROR_INVAL;
                }
            }
            //# ifdef JSMN_STRICT
            /* In strict mode primitive must be followed by a comma/object/array */
            parser->pos = start;
            return (int)jsmnerr.JSMN_ERROR_PART;
            //#endif

            found:
            if (tokens == null)
            {
                parser->pos--;
                return 0;
            }
            token = jsmn_alloc_token(parser, tokens, num_tokens);
            if (token == null)
            {
                parser->pos = start;
                return (int)jsmnerr.JSMN_ERROR_NOMEM;
            }
            jsmn_fill_token(token, jsmntype_t.JSMN_PRIMITIVE, (int)start, (int)parser->pos);
            //# ifdef JSMN_PARENT_LINKS
            token->parent = parser->toksuper;
            //#endif
            parser->pos--;
            return 0;
        }

        /**
         * Fills next token with JSON string.
         */
        unsafe static int jsmn_parse_string(jsmn_parser* parser, char* js,
                int len, jsmntok_t* tokens, int num_tokens)
        {
            jsmntok_t* token;

            int start = (int)parser->pos;

            parser->pos++;

            /* Skip starting quote */
            for (; parser->pos < len && js[parser->pos] != '\0'; parser->pos++)
            {
                char c = js[parser->pos];

                /* Quote: end of string */
                if (c == '\"')
                {
                    if (tokens == null)
                    {
                        return 0;
                    }
                    token = jsmn_alloc_token(parser, tokens, num_tokens);
                    if (token == null)
                    {
                        parser->pos = start;
                        return (int)jsmnerr.JSMN_ERROR_NOMEM;
                    }
                    jsmn_fill_token(token, jsmntype_t.JSMN_STRING, start + 1, parser->pos);
                    //# ifdef JSMN_PARENT_LINKS
                    token->parent = parser->toksuper;
                    //#endif
                    return 0;
                }

                /* Backslash: Quoted symbol expected */
                if (c == '\\' && parser->pos + 1 < len)
                {
                    int i;
                    parser->pos++;
                    switch (js[parser->pos])
                    {
                        /* Allowed escaped symbols */
                        case '\"':
                        case '/':
                        case '\\':
                        case 'b':
                        case 'f':
                        case 'r':
                        case 'n':
                        case 't':
                            break;
                        /* Allows escaped symbol \uXXXX */
                        case 'u':
                            parser->pos++;
                            for (i = 0; i < 4 && parser->pos < len && js[parser->pos] != '\0'; i++)
                            {
                                /* If it isn't a hex character we have an error */
                                if (!((js[parser->pos] >= 48 && js[parser->pos] <= 57) || /* 0-9 */
                                            (js[parser->pos] >= 65 && js[parser->pos] <= 70) || /* A-F */
                                            (js[parser->pos] >= 97 && js[parser->pos] <= 102)))
                                { /* a-f */
                                    parser->pos = start;
                                    return (int)jsmnerr.JSMN_ERROR_INVAL;
                                }
                                parser->pos++;
                            }
                            parser->pos--;
                            break;
                        /* Unexpected symbol */
                        default:
                            parser->pos = start;
                            return (int)jsmnerr.JSMN_ERROR_INVAL;
                    }
                }
            }
            parser->pos = start;
            return (int)jsmnerr.JSMN_ERROR_PART;
        }

        /**
         * Parse JSON string and fill tokens.
         */
        unsafe static int jsmn_parse(jsmn_parser* parser, char* js, int len,
                jsmntok_t* tokens, int num_tokens)
        {
            int r;
            int i;
            jsmntok_t* token;
            int count = (int)parser->toknext;

            for (; parser->pos < len && js[parser->pos] != '\0'; parser->pos++)
            {
                char c;
                jsmntype_t type;

                c = js[parser->pos];
                switch (c)
                {
                    case '{':
                    case '[':
                        count++;
                        if (tokens == null)
                        {
                            break;
                        }
                        token = jsmn_alloc_token(parser, tokens, (int)num_tokens);
                        if (token == null)
                            return (int)jsmnerr.JSMN_ERROR_NOMEM;
                        if (parser->toksuper != -1)
                        {
                            tokens[parser->toksuper].size++;
                            //# ifdef JSMN_PARENT_LINKS
                            token->parent = parser->toksuper;
                            //#endif
                        }
                        token->type = (c == '{' ? jsmntype_t.JSMN_OBJECT : jsmntype_t.JSMN_ARRAY);
                        token->start = parser->pos;
                        parser->toksuper = (int)parser->toknext - 1;
                        break;
                    case '}':
                    case ']':
                        if (tokens == null)
                            break;
                        type = (c == '}' ? jsmntype_t.JSMN_OBJECT : jsmntype_t.JSMN_ARRAY);
                        //# ifdef JSMN_PARENT_LINKS
                        if (parser->toknext < 1)
                        {
                            return (int)jsmnerr.JSMN_ERROR_INVAL;
                        }
                        token = &tokens[parser->toknext - 1];
                        for (; ; )
                        {
                            if (token->start != -1 && token->end == -1)
                            {
                                if (token->type != type)
                                {
                                    return (int)jsmnerr.JSMN_ERROR_INVAL;
                                }
                                token->end = parser->pos + 1;
                                parser->toksuper = token->parent;
                                break;
                            }
                            if (token->parent == -1)
                            {
                                if (token->type != type || parser->toksuper == -1)
                                {
                                    return (int)jsmnerr.JSMN_ERROR_INVAL;
                                }
                                break;
                            }
                            token = &tokens[token->parent];
                        }
                        //#else
                        //                    for (i = parser->toknext - 1; i >= 0; i--)
                        //                    {
                        //                        token = &tokens[i];
                        //                        if (token->start != -1 && token->end == -1)
                        //                        {
                        //                            if (token->type != type)
                        //                            {
                        //                                return jsmnerr.JSMN_ERROR_INVAL;
                        //                            }
                        //                            parser->toksuper = -1;
                        //                            token->end = parser->pos + 1;
                        //                            break;
                        //                        }
                        //                    }
                        //                    /* Error if unmatched closing bracket */
                        //                    if (i == -1) return jsmnerr.JSMN_ERROR_INVAL;
                        //                    for (; i >= 0; i--)
                        //                    {
                        //                        token = &tokens[i];
                        //                        if (token->start != -1 && token->end == -1)
                        //                        {
                        //                            parser->toksuper = i;
                        //                            break;
                        //                        }
                        //                    }
                        //#endif
                        break;
                    case '\"':
                        r = jsmn_parse_string(parser, js, len, tokens, num_tokens);
                        if (r < 0) return r;
                        count++;
                        if (parser->toksuper != -1 && tokens != null)
                            tokens[parser->toksuper].size++;
                        break;
                    case '\t':
                    case '\r':
                    case '\n':
                    case ' ':
                        break;
                    case ':':
                        parser->toksuper = parser->toknext - 1;
                        break;
                    case ',':
                        if (tokens != null && parser->toksuper != -1 &&
                                tokens[parser->toksuper].type != jsmntype_t.JSMN_ARRAY &&
                                tokens[parser->toksuper].type != jsmntype_t.JSMN_OBJECT)
                        {
                            //# ifdef JSMN_PARENT_LINKS
                            parser->toksuper = tokens[parser->toksuper].parent;
                            //#else
                            //                        for (i = parser->toknext - 1; i >= 0; i--)
                            //                        {
                            //                            if (tokens[i].type == jsmntype_t.JSMN_ARRAY || tokens[i].type == jsmntype_t.JSMN_OBJECT)
                            //                            {
                            //                                if (tokens[i].start != -1 && tokens[i].end == -1)
                            //                                {
                            //                                    parser->toksuper = i;
                            //                                    break;
                            //                                }
                            //                            }
                            //                        }
                            //#endif
                        }
                        break;
                    //# ifdef JSMN_STRICT
                    /* In strict mode primitives are: numbers and booleans */
                    case '-':
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                    case 't':
                    case 'f':
                    case 'n':
                        /* And they must not be keys of the object */
                        if (tokens != null && parser->toksuper != -1)
                        {
                            jsmntok_t* t = &tokens[parser->toksuper];
                            if (t->type == jsmntype_t.JSMN_OBJECT ||
                                    (t->type == jsmntype_t.JSMN_STRING && t->size != 0))
                            {
                                return (int)jsmnerr.JSMN_ERROR_INVAL;
                            }
                        }
                        //#else
                        //                /* In non-strict mode every unquoted value is a primitive */
                        //                default:
                        //#endif
                        r = jsmn_parse_primitive(parser, js, len, tokens, num_tokens);
                        if (r < 0) return r;
                        count++;
                        if (parser->toksuper != -1 && tokens != null)
                            tokens[parser->toksuper].size++;
                        break;

                    //# ifdef JSMN_STRICT
                    /* Unexpected char in strict mode */
                    default:
                        return (int)jsmnerr.JSMN_ERROR_INVAL;
                        //#endif
                }
            }

            if (tokens != null)
            {
                for (i = parser->toknext - 1; i >= 0; i--)
                {
                    /* Unmatched opened object or array */
                    if (tokens[i].start != -1 && tokens[i].end == -1)
                    {
                        return (int)jsmnerr.JSMN_ERROR_PART;
                    }
                }
            }

            return count;
        }

        /**
         * Creates a new parser based over a given  buffer with an array of tokens
         * available.
         */
        unsafe static void jsmn_init(jsmn_parser* parser)
        {
            parser->pos = 0;
            parser->toknext = 0;
            parser->toksuper = -1;
        }
    }
}