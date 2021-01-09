# cloudwatch-log-pump

# Configuration

* Relative times format: `"-D:hh:mm:ss.FFFFFFFFF"` - 
  based on "General round-trip pattern" from [NodaTime Duration](https://nodatime.org/3.0.x/userguide/duration-patterns). 
  Examples:
  * 2 days ago: `"-2:00:00:00.0"`
  * 4 hours ago: `"-0:04:00:00.0"`
    
* Absolute time format: `"uuuu-MM-ddTHH:mm:ss'Z'"` - 
  ISO-8601 representation in UTC, based on [NodaTime Instant](https://nodatime.org/3.0.x/userguide/instant-patterns). 
  Examples:
  * Beginning of January 1st 2021: `""`
    

    
