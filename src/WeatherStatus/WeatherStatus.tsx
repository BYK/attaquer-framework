import "./style.css";
import { WeatherOutput } from "zebar";
import { Component } from "solid-js";
import * as zebar from "zebar";

interface WeatherStatusProps {
    weather: WeatherOutput;
}

const statusLabels: Record<string, string> = {
    clear_day: "Clear",
    clear_night: "Clear",
    cloudy_day: "Cloudy",
    cloudy_night: "Cloudy",
    light_rain_day: "Light rain",
    light_rain_night: "Light rain",
    heavy_rain_day: "Heavy rain",
    heavy_rain_night: "Heavy rain",
    snow_day: "Snow",
    snow_night: "Snow",
    thunder_day: "Thunderstorm",
    thunder_night: "Thunderstorm",
};

const WeatherStatus: Component<WeatherStatusProps> = (props) => {
    const getWeatherIcon = (status: string) => {
        switch (status) {
            case 'clear_day':
              return <i class="nf nf-weather-day_sunny"></i>;
            case 'clear_night':
              return <i class="nf nf-weather-night_clear"></i>;
            case 'cloudy_day':
              return <i class="nf nf-weather-day_cloudy"></i>;
            case 'cloudy_night':
              return <i class="nf nf-weather-night_alt_cloudy"></i>;
            case 'light_rain_day':
              return <i class="nf nf-weather-day_sprinkle"></i>;
            case 'light_rain_night':
              return <i class="nf nf-weather-night_alt_sprinkle"></i>;
            case 'heavy_rain_day':
              return <i class="nf nf-weather-day_rain"></i>;
            case 'heavy_rain_night':
              return <i class="nf nf-weather-night_alt_rain"></i>;
            case 'snow_day':
              return <i class="nf nf-weather-day_snow"></i>;
            case 'snow_night':
              return <i class="nf nf-weather-night_alt_snow"></i>;
            case 'thunder_day':
              return <i class="nf nf-weather-day_lightning"></i>;
            case 'thunder_night':
              return <i class="nf nf-weather-night_alt_lightning"></i>;
          }
    };

    const tooltip = () => {
        const w = props.weather;
        if (!w) return "";
        const label = statusLabels[w.status] ?? w.status;
        const wind = Math.round(w.windSpeed);
        return `${label} · ${Math.round(w.celsiusTemp)}°C · Wind ${wind} km/h`;
    };

    const openForecast = () => {
        // Open-Meteo doesn't have a consumer-facing forecast page;
        // use wttr.in which auto-detects location and shows a nice forecast
        zebar.shellExec("start", "https://wttr.in");
    };

    return (
        <div class="template weather" title={tooltip()} onClick={openForecast}>
            {getWeatherIcon(props.weather?.status)}
            {Math.round(props.weather?.celsiusTemp)}°C
        </div>
    );
};

export default WeatherStatus;
