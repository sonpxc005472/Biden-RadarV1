using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Biden.Radar
{
    public static class Extensions
    {
        public static TModel GetOptions<TModel>(this IConfiguration configuration, string section) where TModel : new()
        {
            var model = new TModel();
            configuration.GetSection(section).Bind(model);
            return model;
        }
        
        public static int ToInt(this Enum value)
        {
            return Convert.ToInt32(value);
        }

        public static Guid ToGuid(this string value)
        {
            return string.IsNullOrWhiteSpace(value) ? Guid.Empty : Guid.Parse(value);
        }

        public static string ToIntString(this int value, int numberPadleft = 2)
        {
            return value.ToString().PadLeft(numberPadleft, '0');
        }

        public static string SplitCamelCase(this string input)
        {
            return Regex.Replace(input, "(\\B[A-Z])", " $1");
        }

        public static string FormatToSQLParam(this string input)
        {
            return $"p_{input}";
        }

        /// <summary>
        /// Remove characters from string starting from the nth occurrence of seperating character
        /// </summary>
        /// <param name="input" value="additional\contact\number"></param>
        /// <param name="seperator" value="\"></param>
        /// <param name="occurNo" value="2"></param>
        /// <returns value="additional\contact"></returns>
        public static string GetStringToNthOccurringCharacter(this string input, string seperator, int occurNo)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }
            if (input.IndexOf(seperator) > 0)
            {
                var inputList = input.Split(seperator).Take(occurNo).ToList();
                return string.Join(seperator, inputList);
            }
            return input;
        }


        #region CurrencyFormatting

        public static string ToCurrencyFormatWithFraction(this decimal val, int digits = 2)
        {
            if (digits <= 1)
            {
                digits = 1;
            }
            var convertingFormat = string.Concat("{0:#,0.", new string('0', digits), "}");
            return val == 0 ? "0" : string.Format(convertingFormat, val);
        }

        public static string ToTruncatedStringWithFraction(this decimal val, int digits = 1)
        {
            if (digits <= 1)
            {
                digits = 1;
            }
            var computeValue = Math.Truncate(val * (int)Math.Pow(10, digits)) / (int)Math.Pow(10, digits);

            return val == 0 ? "0" : computeValue.ToString();
        }

        public static string ToCurrencyFormatWithFraction(this decimal? val, int digits = 2)
        {
            return val == null ? "0" : val.Value.ToCurrencyFormatWithFraction(digits);
        }

        public static string ToCurrencyFormatWithFraction(this decimal? val, string valueWhenNull, int digits = 2)
        {
            return val == null ? valueWhenNull : val.Value.ToCurrencyFormatWithFraction(digits);
        }

        public static string ToCurrencyFormatWithSeperator(this decimal val)
        {
            return val == 0 ? "0" : string.Format("{0:n}", val);
        }

        public static string ToCurrencyFormatWithSeperator(this decimal? val)
        {
            return (val ?? 0).ToCurrencyFormatWithSeperator();
        }

        #endregion

        public static string CapitalizeFirstLetter(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLower());
        }

        public static string FormatNumber(this decimal num)
        {
            if (num >= 1000000000000)
                return (num / 1000000000000).ToString("0.##") + "T";
            else if (num >= 1000000000)
                return (num / 1000000000).ToString("0.##") + "B";
            else if (num >= 1000000)
                return (num / 1000000).ToString("0.##") + "M";
            else if (num >= 1000)
                return (num / 1000).ToString("0.##") + "K";
            else
                return num.ToString("0.##");
        }

    }
}
