package com.example.jamkkaebi.global.config;

import com.example.jamkkaebi.user.web.UserActivityInterceptor;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.servlet.config.annotation.InterceptorRegistry;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

/** MVC 설정. 게임 API 호출을 접속으로 기록하는 인터셉터를 건다. */
@Configuration
public class WebMvcConfig implements WebMvcConfigurer {

    private final UserActivityInterceptor userActivityInterceptor;

    public WebMvcConfig(UserActivityInterceptor userActivityInterceptor) {
        this.userActivityInterceptor = userActivityInterceptor;
    }

    @Override
    public void addInterceptors(InterceptorRegistry registry) {
        registry.addInterceptor(userActivityInterceptor).addPathPatterns("/api/**");
    }
}
