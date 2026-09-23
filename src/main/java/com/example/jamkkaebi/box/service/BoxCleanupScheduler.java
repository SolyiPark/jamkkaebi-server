package com.example.jamkkaebi.box.service;

import com.example.jamkkaebi.box.config.BoxProperties;
import com.example.jamkkaebi.box.repository.BoxOpenLogRepository;
import com.example.jamkkaebi.global.time.GameClock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

/**
 * 오래된 상자 기록을 지운다.
 *
 * <p>이 표는 사용자당 매일 늘어난다. 멱등 키로서의 수명은 24시간이고 그 뒤는 로그여서
 * 보관 기간을 넘긴 행은 남겨 둘 이유가 없다.
 */
@Component
public class BoxCleanupScheduler {

    private static final Logger log = LoggerFactory.getLogger(BoxCleanupScheduler.class);

    private static final long INTERVAL_MS = 24 * 60 * 60 * 1000L;

    private final BoxOpenLogRepository openLogRepository;
    private final BoxProperties properties;
    private final GameClock gameClock;

    public BoxCleanupScheduler(BoxOpenLogRepository openLogRepository,
                               BoxProperties properties,
                               GameClock gameClock) {
        this.openLogRepository = openLogRepository;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    @Scheduled(fixedDelay = INTERVAL_MS, initialDelay = INTERVAL_MS)
    @Transactional
    public void purgeOldLogs() {
        int removed = openLogRepository.deleteOlderThan(
                gameClock.today().minusDays(properties.logRetention().toDays()));
        if (removed > 0) {
            log.debug("보관 기간이 지난 상자 기록 {}건을 정리했습니다.", removed);
        }
    }
}
