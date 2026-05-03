namespace INcheonChurchWeb.Models
{
    public static class DateHelper
    {
        public static int CalculateFiscalYear(DateTime date)
        {
            int calendarYear = date.Year;
            DateTime currentYearEnd = GetFiscalYearEndDate(calendarYear);

            if (date > currentYearEnd)
            {
                return calendarYear + 1;
            }

            DateTime lastYearEnd = GetFiscalYearEndDate(calendarYear - 1);
            return date > lastYearEnd ? calendarYear : calendarYear - 1;
        }

        public static int GetQuarter(DateTime date)
        {
            if (date.Month >= 3 && date.Month <= 5) return 2;
            if (date.Month >= 6 && date.Month <= 8) return 3;
            if (date.Month == 9 || date.Month == 10) return 4;
            if (date.Month == 12 || date.Month == 1 || date.Month == 2) return 1;

            DateTime nov4thSunday = GetFiscalYearEndDate(date.Year);
            return date > nov4thSunday ? 1 : 4;
        }

        public static DateTime GetFiscalYearEndDate(int year)
        {
            DateTime nov1 = new DateTime(year, 11, 1);
            int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)nov1.DayOfWeek + 7) % 7;
            return nov1.AddDays(daysUntilSunday + 21);
        }
    }
}
