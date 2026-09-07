package com.example.jamkkaebi;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.ConfigurationPropertiesScan;
import org.springframework.scheduling.annotation.EnableScheduling;

@SpringBootApplication
@EnableScheduling // 만료된 로그인 세션·인계 코드 정리 작업을 돌린다.
@ConfigurationPropertiesScan // JwtProperties·AuthProperties 를 별도 등록 없이 바인딩한다.
public class JamkkaebiApplication {

    public static void main(String[] args) {
        SpringApplication.run(JamkkaebiApplication.class, args);
    }

}
