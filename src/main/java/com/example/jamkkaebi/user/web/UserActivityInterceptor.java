package com.example.jamkkaebi.user.web;

import com.example.jamkkaebi.global.security.AuthPrincipal;
import com.example.jamkkaebi.user.service.UserActivityService;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.jspecify.annotations.NonNull;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.stereotype.Component;
import org.springframework.web.servlet.HandlerInterceptor;

/**
 * 인증된 게임 API 호출을 접속으로 기록한다.
 *
 * <p>{@code preHandle} 이 아니라 {@code afterCompletion} 에서 기록한다 — 접속 시점 스냅샷에는 <b>그
 * 요청이 바꾼 상태까지</b> 담겨야 하기 때문이다
 *
 * <p>기록이 실패해도 요청은 이미 처리됐으므로 로그만 남긴다. 접속 기록 때문에 게임 API 가 실패하면 안 된다.
 */
@Component
public class UserActivityInterceptor implements HandlerInterceptor {

    private static final Logger log = LoggerFactory.getLogger(UserActivityInterceptor.class);

    private final UserActivityService userActivityService;

    public UserActivityInterceptor(UserActivityService userActivityService) {
        this.userActivityService = userActivityService;
    }

    @Override
    public void afterCompletion(@NonNull HttpServletRequest request,
                                @NonNull HttpServletResponse response,
                                @NonNull Object handler,
                                Exception exception) {
        Authentication authentication = SecurityContextHolder.getContext().getAuthentication();
        if (authentication == null || !(authentication.getPrincipal() instanceof AuthPrincipal principal)) {
            return;
        }
        try {
            userActivityService.recordActivity(principal.friendCode());
        } catch (RuntimeException recordFailure) {
            log.warn("접속 기록에 실패했습니다.", recordFailure);
        }
    }
}
