package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.UserActivityRecordedEvent;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 접속을 기록한다
 *
 * <p>인증된 요청이 들어올 때마다 부르지만 <b>매번 DB 에 쓰지 않는다 — 자정 직후의 첫 요청이 그날의 출석으로 잡혀야 한다.
 */
@Service
public class UserActivityService {

    // 같은 사용자의 접속을 다시 기록하기까지의 최소 간격
    static final Duration TOUCH_INTERVAL = Duration.ofMinutes(1);

    private final UserRepository userRepository;
    private final GameClock gameClock;
    private final ApplicationEventPublisher eventPublisher;
    private final TransactionTemplate transaction;
    // 친구 코드 → 마지막으로 기록한 시각. 기록을 건너뛸지 DB 조회 없이 판단하기 위한 캐시다.
    private final Map<String, LocalDateTime> lastTouches = new ConcurrentHashMap<>();

    public UserActivityService(UserRepository userRepository,
                               GameClock gameClock,
                               ApplicationEventPublisher eventPublisher,
                               PlatformTransactionManager transactionManager) {
        this.userRepository = userRepository;
        this.gameClock = gameClock;
        this.eventPublisher = eventPublisher;
        this.transaction = new TransactionTemplate(transactionManager);
    }

    //사용자 갱신을 커밋한 <b>뒤에</b> 접속했음을 기록한다.
    public void recordActivity(String friendCode) {
        LocalDateTime now = gameClock.now();
        LocalDateTime lastTouch = lastTouches.get(friendCode);
        if (lastTouch != null && !isTouchDue(lastTouch, now)) {
            return;
        }

        Long userId = transaction.execute(status -> userRepository.findByFriendCode(friendCode)
                .map(user -> {
                    user.recordActivity(now);
                    return user.getId();
                })
                .orElse(null));
        lastTouches.put(friendCode, now);

        if (userId != null) {
            eventPublisher.publishEvent(new UserActivityRecordedEvent(userId, now));
        }
    }

    private boolean isTouchDue(LocalDateTime lastTouch, LocalDateTime now) {
        return !lastTouch.toLocalDate().equals(now.toLocalDate())
                || Duration.between(lastTouch, now).compareTo(TOUCH_INTERVAL) >= 0;
    }

    // 간격이 지난 캐시 항목을 비운다.
    @Scheduled(fixedDelay = 10 * 60 * 1000L, initialDelay = 10 * 60 * 1000L)
    public void forgetStaleTouches() {
        LocalDateTime threshold = gameClock.now().minus(TOUCH_INTERVAL);
        lastTouches.values().removeIf(touchedAt -> touchedAt.isBefore(threshold));
    }
}
