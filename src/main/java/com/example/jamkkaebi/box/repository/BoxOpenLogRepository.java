package com.example.jamkkaebi.box.repository;

import com.example.jamkkaebi.box.domain.BoxOpenLog;
import com.example.jamkkaebi.box.domain.BoxSource;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDate;
import java.util.Optional;

public interface BoxOpenLogRepository extends JpaRepository<BoxOpenLog, Long> {

    Optional<BoxOpenLog> findByUserIdAndRequestId(Long userId, String requestId);

    boolean existsByUserIdAndSourceAndOpenedDate(Long userId, BoxSource source, LocalDate openedDate);

    long countByUserIdAndSourceAndOpenedDate(Long userId, BoxSource source, LocalDate openedDate);

    // 오래된 기록 삭제
    @Modifying(flushAutomatically = true)
    @Query("DELETE FROM BoxOpenLog b WHERE b.openedDate < :before")
    int deleteOlderThan(@Param("before") LocalDate before);
}
