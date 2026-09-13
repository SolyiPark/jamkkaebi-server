package com.example.jamkkaebi.friend.repository;

import com.example.jamkkaebi.friend.domain.VisitLog;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDate;
import java.util.List;
import java.util.Optional;

public interface VisitLogRepository extends JpaRepository<VisitLog, Long> {

    boolean existsByVisitorIdAndVisitDateAndRewardedTrue(Long visitorId, LocalDate visitDate);

    Optional<VisitLog> findByVisitorIdAndHostIdAndVisitDate(Long visitorId, Long hostId, LocalDate visitDate);

    @Query("SELECT v.hostId FROM VisitLog v WHERE v.visitorId = :visitorId AND v.visitDate = :visitDate")
    List<Long> findHostIdsVisited(@Param("visitorId") Long visitorId, @Param("visitDate") LocalDate visitDate);

    @Modifying(flushAutomatically = true)
    @Query("DELETE FROM VisitLog v WHERE v.visitDate < :before")
    int deleteOlderThan(@Param("before") LocalDate before);
}
